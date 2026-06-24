using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class InterpreterTests
{
    [Fact]
    public void Execute_VariableDeclaration_WritesToContext()
    {
        var context = Execute("var a = 1;");

        Assert.Equal(1d, context.Get<double>("a"));
    }

    [Fact]
    public void Execute_Assignment_WritesToContext()
    {
        var context = Execute("result = \"OK\";");

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_MathExpression_WritesResult()
    {
        var context = Execute("result = 10 - 4;");

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_OperatorPrecedence_EvaluatesMultiplicationBeforeAddition()
    {
        var context = Execute("result = 1 + 2 * 3;");

        Assert.Equal(7d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_StringConcatenation_ConcatenatesStringsAndValues()
    {
        var context = Execute("""
            var name = "Mick";
            result = "Hello " + name + " #" + 1;
            """);

        Assert.Equal("Hello Mick #1", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ComparisonExpression_WritesBoolResult()
    {
        var context = Execute("result = 3 >= 2;");

        Assert.True(context.Get<bool>("result"));
    }

    [Theory]
    [InlineData("true and true", true)]
    [InlineData("true and false", false)]
    [InlineData("false or true", true)]
    [InlineData("false or false", false)]
    [InlineData("1 < 2 and 3 < 4", true)]
    [InlineData("1 > 2 or 3 < 4", true)]
    public void Execute_BooleanOperators_WriteBoolResult(string expression, bool expected)
    {
        var context = Execute($"result = {expression};");

        Assert.Equal(expected, context.Get<bool>("result"));
    }

    [Fact]
    public void Execute_IfTrueBranch_ExecutesThenBranch()
    {
        var context = Execute("""
            var enabled = true;
            if enabled then:
                result = "OK";
            endif
            """);

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_IfFalseBranchWithoutElse_SkipsThenBranch()
    {
        var context = Execute("""
            result = "initial";
            if false then:
                result = "changed";
            endif
            """);

        Assert.Equal("initial", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_IfElseBranch_ExecutesElseBranch()
    {
        var context = Execute("""
            if false then:
                result = "OK";
            else:
                result = "NG";
            endif
            """);

        Assert.Equal("NG", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_UndefinedVariable_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("result = missing;"));
    }

    [Fact]
    public void Execute_InvalidTypeOperation_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("result = true - 1;"));
    }

    [Theory]
    [InlineData("true and 1", "and")]
    [InlineData("false or 1", "or")]
    public void Execute_BooleanOperatorWithNonBoolOperand_ThrowsRuntimeException(string expression, string tokenText)
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute($"result = {expression};"));

        Assert.Contains("bool operands", exception.Message);
        Assert.Equal(tokenText, exception.TokenText);
    }

    [Fact]
    public void Execute_FunctionCallPipeline_WorksWithToString()
    {
        var context = Execute("result = ToString(123);");

        Assert.Equal("123", context.Get<string>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        var engine = new RuleScriptEngine();
        var context = new RuntimeContext();

        engine.Execute(script, context);

        return context;
    }
}
