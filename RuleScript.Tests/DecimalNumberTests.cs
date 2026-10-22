using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class DecimalNumberTests
{
    [Fact]
    public void DecimalArithmetic_IsExactForBaseTenFractions()
    {
        var context = new RuleScriptEngine().Execute("""
            var sum = 0.1 + 0.2;
            var equal = 0.1 + 0.2 == 0.3;
            var greater = 0.1 + 0.2 > 0.3;
            var total = 0;
            foreach i in Range(0, 10):
                total += 0.1;
            endforeach
            var price = 19.99 * 3;
            """);

        Assert.Equal(0.3m, context.Get("sum"));
        Assert.True(context.Get<bool>("equal"));
        Assert.False(context.Get<bool>("greater"));
        Assert.Equal(1m, context.Get("total"));
        Assert.Equal(59.97m, context.Get("price"));
    }

    [Fact]
    public void Literals_AreDecimals()
    {
        var context = new RuleScriptEngine().Execute("""
            var whole = 519;
            var fraction = 2.5;
            var negative = -3;
            var type = TypeOf(2.5);
            """);

        Assert.IsType<decimal>(context.Get("whole"));
        Assert.Equal(2.5m, context.Get("fraction"));
        Assert.Equal(-3m, context.Get("negative"));
        Assert.Equal("number", context.Get<string>("type"));
    }

    [Theory]
    [InlineData("ToString(1.50)", "1.5")]
    [InlineData("ToString(2.0)", "2")]
    [InlineData("ToString(1.5 * 2)", "3")]
    [InlineData("ToString(100)", "100")]
    [InlineData("ToString(0.000001)", "0.000001")]
    [InlineData("`${1.50}`", "1.5")]
    [InlineData("\"total: \" + 2.50", "total: 2.5")]
    [InlineData("Join(\",\", [1.0, 2.50])", "1,2.5")]
    [InlineData("JsonStringify({ a: 1.50, b: [2.0] })", "{\"a\":1.5,\"b\":[2]}")]
    [InlineData("FormatNumber(1.5, \"0.00\")", "1.50")]
    public void NumberText_DropsTrailingZeros(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("1 / 0", "Division by zero.")]
    [InlineData("5 % 0", "Division by zero.")]
    [InlineData("79228162514264337593543950335 * 2", "Arithmetic result is outside the range of RuleScript numbers.")]
    [InlineData("-79228162514264337593543950335 - 1", "Arithmetic result is outside the range of RuleScript numbers.")]
    public void ArithmeticErrors_StopTheScript(string expression, string message)
    {
        var exception = Assert.Throws<RuntimeException>(() => Evaluate(expression));

        Assert.Contains(message, exception.Message);
        Assert.Equal(1, exception.Line);
    }

    [Fact]
    public void DivisionByZero_InCompoundAssignmentStopsTheScript()
    {
        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("""
            var total = 10;
            total /= 0;
            """));

        Assert.Contains("Division by zero.", exception.Message);
        Assert.Equal(2, exception.Line);
    }

    [Fact]
    public void LiteralOutsideDecimalRange_IsASyntaxError()
    {
        var analysis = new RuleScriptEngine().TryAnalyze("var big = 100000000000000000000000000000;");

        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Severity == RuleScriptDiagnosticSeverity.Error
            && diagnostic.Message.EndsWith("Number literal is outside the range of RuleScript numbers.", StringComparison.Ordinal));
    }

    [Fact]
    public void Division_Uses28SignificantDigits()
    {
        var context = new RuleScriptEngine().Execute("""
            var third = 1 / 3;
            var half = 10 / 4;
            """);

        Assert.Equal(0.3333333333333333333333333333m, context.Get("third"));
        Assert.Equal(2.5m, context.Get("half"));
    }

    [Fact]
    public void HostDoubles_ConvertToDecimalsWhenUsed()
    {
        var context = new RuntimeContext();
        context.Set("weight", 0.1d);
        context.Set("count", 3);

        new RuleScriptEngine().Execute("""
            var equal = weight + 0.2 == 0.3;
            var total = count * 1.5;
            var same = weight;
            """, context);

        Assert.True(context.Get<bool>("equal"));
        Assert.Equal(4.5m, context.Get("total"));
        Assert.Equal(0.1d, context.Get("same"));
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(1e300, "1E+300")]
    public void HostDoubles_WithoutDecimalRepresentation_StopTheScript(double value, string text)
    {
        var context = new RuntimeContext();
        context.Set("reading", value);

        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("var result = reading + 1;", context));

        Assert.Contains($"Number value {text} is outside the range of RuleScript numbers.", exception.Message);
    }

    [Fact]
    public void HostFunctions_ReceiveDecimals_AndTypedParametersConvert()
    {
        var engine = new RuleScriptEngine();
        object? received = null;
        engine.RegisterFunction("Capture", args =>
        {
            received = args[0];
            return null;
        });
        engine.RegisterFunctions(new MathHost());

        var context = engine.Execute("""
            Capture(1.25);
            var half = Half(3);
            var next = half + 1;
            """);

        Assert.Equal(1.25m, received);
        Assert.Equal(1.5d, context.Get("half"));
        Assert.Equal(2.5m, context.Get("next"));
    }

    [Fact]
    public void Get_ConvertsNumbersToTheRequestedType()
    {
        var context = new RuleScriptEngine().Execute("""
            var whole = 2.0 * 3;
            var fraction = 2.5;
            var index = Range(0, 3)[2];
            """);

        Assert.Equal(6, context.Get<int>("whole"));
        Assert.Equal(6L, context.Get<long>("whole"));
        Assert.Equal(2.5d, context.Get<double>("fraction"));
        Assert.Equal(2.5f, context.Get<float>("fraction"));
        Assert.Equal(2m, context.Get<decimal>("index"));
        Assert.Equal(6, context.Get<int?>("whole"));
    }

    [Fact]
    public void Get_RejectsFractionsForIntegerTypes()
    {
        var context = new RuleScriptEngine().Execute("var fraction = 2.5;");

        var exception = Assert.Throws<InvalidCastException>(() => context.Get<int>("fraction"));

        Assert.Contains("fractional part", exception.Message);
        Assert.Equal(-1, context.GetOrDefault("fraction", -1));
        Assert.Equal(2.5d, context.GetOrDefault("fraction", 0d));
        Assert.Throws<OverflowException>(() => new RuleScriptEngine().Execute("var big = 300;").Get<byte>("big"));
    }

    [Fact]
    public void JsonNumbers_KeepTheirPrecision()
    {
        var context = new RuleScriptEngine().Execute("""
            var data = JsonParse("{ \"w\": 0.1, \"precise\": 0.10000000000000000001 }");
            var equal = data.w + 0.2 == 0.3;
            var precise = data.precise * 10;
            """);

        Assert.True(context.Get<bool>("equal"));
        Assert.Equal(1.0000000000000000001m, context.Get("precise"));
    }

    [Fact]
    public void JsonNumberOutsideDecimalRange_Throws()
    {
        var exception = Assert.Throws<RuntimeException>(() => Evaluate("JsonParse(\"[1e300]\")"));

        Assert.Contains("JsonParse number 1e300 is outside the range of RuleScript numbers.", exception.Message);
    }

    [Theory]
    [InlineData("Pow(2, 10)", "1024")]
    [InlineData("Sqrt(2)", "1.4142135623731")]
    [InlineData("Abs(-1.25)", "1.25")]
    [InlineData("Round(2.345)", "2")]
    [InlineData("Max(0.1 + 0.2, 0.3)", "0.3")]
    [InlineData("Sum([0.1, 0.2, 0.3])", "0.6")]
    [InlineData("Average([0.1, 0.2])", "0.15")]
    [InlineData("DateDiff(\"2026-10-22T00:00:00\", \"2026-10-22T08:00:00\", \"day\")", "0.3333333333333333333333333333")]
    public void Builtins_ReturnDecimals(string expression, string expected)
    {
        Assert.IsType<decimal>(Evaluate(expression));
        Assert.Equal(expected, Evaluate($"ToString({expression})"));
    }

    [Fact]
    public void Pow_ResultOutsideDecimalRange_Throws()
    {
        var exception = Assert.Throws<RuntimeException>(() => Evaluate("Pow(10, 30)"));

        Assert.Contains("Pow result value 1E+30 is outside the range of RuleScript numbers.", exception.Message);
    }

    [Fact]
    public void SwitchCases_CompareDecimalValues()
    {
        var context = new RuleScriptEngine().Execute("""
            var result = "none";
            switch 0.1 + 0.2:
                case 0.3:
                    result = "matched";
                default:
                    result = "default";
            endswitch
            """);

        Assert.Equal("matched", context.Get<string>("result"));
    }

    private sealed class MathHost
    {
        [RuleScriptFunction]
        public double Half(double value) => value / 2;
    }

    private static object? Evaluate(string expression)
    {
        return new RuleScriptEngine().Execute($"var result = {expression};").Get("result");
    }
}
