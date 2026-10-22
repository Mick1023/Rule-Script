using System.Collections;
using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ExtendedBuiltinTests
{
    [Theory]
    [InlineData("""IndexOf("a-b-c", "-")""", 1)]
    [InlineData("""IndexOf("abc", "x")""", -1)]
    [InlineData("""IndexOf("abc", "")""", 0)]
    [InlineData("""LastIndexOf("a-b-c", "-")""", 3)]
    [InlineData("""LastIndexOf("abc", "x")""", -1)]
    [InlineData("""LastIndexOf("abc", "")""", 3)]
    [InlineData("""IndexOf(12345, 34)""", 2)]
    public void IndexOf_UsesOrdinalSearch(string call, int expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Theory]
    [InlineData("""PadLeft("7", 3)""", "  7")]
    [InlineData("""PadLeft(7, 3, "0")""", "007")]
    [InlineData("""PadRight("ab", 4, ".")""", "ab..")]
    [InlineData("""PadRight("abcdef", 3)""", "abcdef")]
    public void Pad_PadsToTotalWidth(string call, string expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Theory]
    [InlineData("""PadLeft("7", 3, "00")""", "PadLeft argument 'padding' must be a single-character string.")]
    [InlineData("""PadLeft("7", -1)""", "PadLeft argument 'totalWidth' must be between 0 and 1000000.")]
    [InlineData("""PadRight("7", 2000000)""", "PadRight argument 'totalWidth' must be between 0 and 1000000.")]
    [InlineData("""PadRight("7", 1.5)""", "PadRight argument 'totalWidth' must be an int value.")]
    public void Pad_InvalidArguments_Throw(string call, string message)
    {
        AssertThrows(call, message);
    }

    [Theory]
    [InlineData("""FormatNumber(3.14159, "0.00")""", "3.14")]
    [InlineData("""FormatNumber(1234567, "N0")""", "1,234,567")]
    [InlineData("""FormatNumber(5, "000")""", "005")]
    [InlineData("""FormatNumber(0.5, "P0")""", "50 %")]
    public void FormatNumber_UsesInvariantCulture(string call, string expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Fact]
    public void FormatNumber_KeepsJsonDecimalPrecision()
    {
        Assert.Equal("0.10000000000000000001", Evaluate("""FormatNumber(JsonParse("[0.10000000000000000001]")[0], "0.####################")"""));
    }

    [Fact]
    public void FormatNumber_InvalidFormat_Throws()
    {
        AssertThrows("""FormatNumber(1.5, "D2")""", "FormatNumber format 'D2' is not valid.");
    }

    [Fact]
    public void Regex_MatchesFindsAndReplaces()
    {
        var context = new RuleScriptEngine().Execute("""
            var isLot = RegexIsMatch("LOT-2026-0042", "^LOT-\\d{4}-\\d+$");
            var numbers = RegexMatches("a1b22c333", "\\d+");
            var swapped = RegexReplace("2026-10-22", "(\\d+)-(\\d+)-(\\d+)", "$3/$2/$1");
            var none = RegexMatches("abc", "\\d");
            """);

        Assert.True(context.Get<bool>("isLot"));
        Assert.Equal(["1", "22", "333"], AsList(context.Get("numbers")));
        Assert.Equal("22/10/2026", context.Get<string>("swapped"));
        Assert.Empty(AsList(context.Get("none")));
    }

    [Fact]
    public void Regex_InvalidPattern_Throws()
    {
        AssertThrows("""RegexIsMatch("abc", "(")""", "RegexIsMatch pattern is not valid:");
    }

    [Fact]
    public void Regex_CatastrophicPattern_TimesOut()
    {
        var input = new string('a', 40) + "!";

        AssertThrows($$"""RegexIsMatch("{{input}}", "^(a+)+$")""", "RegexIsMatch timed out after 1 second(s).");
    }

    [Theory]
    [InlineData("Pow(2, 10)", 1024d)]
    [InlineData("Pow(9, 0.5)", 3d)]
    [InlineData("Sqrt(16)", 4d)]
    [InlineData("Sum([1, 2, 3.5])", 6.5d)]
    [InlineData("Sum([])", 0d)]
    [InlineData("Average([1, 2, 3, 4])", 2.5d)]
    public void Math_ComputesNumbers(string call, double expected)
    {
        Assert.Equal((decimal)expected, Evaluate(call));
    }

    [Theory]
    [InlineData("Sqrt(-1)", "Sqrt expects a non-negative number.")]
    [InlineData("Average([])", "Average expects a non-empty array.")]
    [InlineData("""Sum([1, "2"])""", "Sum expects all elements to be numbers.")]
    public void Math_InvalidArguments_Throw(string call, string message)
    {
        AssertThrows(call, message);
    }

    [Theory]
    [InlineData("ArraySlice([1, 2, 3, 4], 1, 3)", new[] { 2, 3 })]
    [InlineData("ArraySlice([1, 2, 3, 4], 2)", new[] { 3, 4 })]
    [InlineData("ArraySlice([1, 2, 3, 4], 4)", new int[0])]
    [InlineData("ArrayDistinct([1, 2, 1, 3, 2])", new[] { 1, 2, 3 })]
    public void Array_ReturnsNewArrays(string call, int[] expected)
    {
        Assert.Equal(expected.Cast<object?>(), AsList(Evaluate(call)).Select(value => (object?)Convert.ToInt32(value)));
    }

    [Theory]
    [InlineData("ArraySlice([1, 2], 3)")]
    [InlineData("ArraySlice([1, 2], 2, 1)")]
    [InlineData("ArraySlice([1, 2], -1)")]
    public void ArraySlice_OutOfRange_Throws(string call)
    {
        AssertThrows(call, "ArraySlice range is outside the array bounds.");
    }

    [Fact]
    public void ArraySlice_DoesNotChangeTheSource()
    {
        var context = new RuleScriptEngine().Execute("""
            var source = [1, 2, 3];
            var part = ArraySlice(source, 0, 2);
            part[0] = 9;
            """);

        Assert.Equal(1, Convert.ToInt32(AsList(context.Get("source"))[0]));
    }

    [Fact]
    public void ArrayDistinct_TreatsEqualNumbersAndNullsAsDuplicates()
    {
        var context = new RuleScriptEngine().Execute("""var result = ArrayDistinct([1, 1.0, "1", null, null, "a", "a"]);""");

        Assert.Equal([1, "1", null, "a"], AsList(context.Get("result")).Select(value => value is decimal number ? (object?)(int)number : value));
    }

    [Fact]
    public void ArrayIndexOf_AndReverse()
    {
        var context = new RuleScriptEngine().Execute("""
            var values = ["a", "b", "c"];
            var found = ArrayIndexOf(values, "b");
            var missing = ArrayIndexOf(values, "z");
            var same = ArrayReverse(values);
            """);

        Assert.Equal(1, context.Get<int>("found"));
        Assert.Equal(-1, context.Get<int>("missing"));
        Assert.Equal(["c", "b", "a"], AsList(context.Get("values")));
        Assert.Same(context.Get("values"), context.Get("same"));
    }

    [Fact]
    public void ObjectRemove_AndMerge()
    {
        var context = new RuleScriptEngine().Execute("""
            var robot = {status: "OK", distance: 519, temp: 40};
            var removed = ObjectRemove(robot, "temp");
            var again = ObjectRemove(robot, "temp");
            var merged = ObjectMerge(robot, {status: "NG", line: 3});
            """);

        Assert.True(context.Get<bool>("removed"));
        Assert.False(context.Get<bool>("again"));
        var robot = Assert.IsAssignableFrom<IDictionary<string, object?>>(context.Get("robot"));
        Assert.Equal(["distance", "status"], robot.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("OK", robot["status"]);
        var merged = Assert.IsAssignableFrom<IDictionary<string, object?>>(context.Get("merged"));
        Assert.Equal("NG", merged["status"]);
        Assert.Equal(3d, Convert.ToDouble(merged["line"]));
        Assert.Equal(519d, Convert.ToDouble(merged["distance"]));
    }

    [Fact]
    public void ObjectRemove_ReadOnlyObject_Throws()
    {
        var context = new RuntimeContext();
        context.Set("config", new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["a"] = 1 }));

        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("""var removed = ObjectRemove(config, "a");""", context));

        Assert.Contains("ObjectRemove expects a mutable object value.", exception.Message);
    }

    [Theory]
    [InlineData("""DateAdd("2026-10-22T10:30:00", 2, "hour")""", "2026-10-22T12:30:00")]
    [InlineData("""DateAdd("2026-10-22", 10, "day")""", "2026-11-01T00:00:00")]
    [InlineData("""DateAdd("2026-01-31", 1, "month")""", "2026-02-28T00:00:00")]
    [InlineData("""DateAdd("2026-10-22T10:30:00", -1, "year")""", "2025-10-22T10:30:00")]
    [InlineData("""DateAdd("2026-10-22T10:30:00", 1.5, "minute")""", "2026-10-22T10:31:30")]
    [InlineData("""DateAdd("2026-10-22T10:30:00", 250, "millisecond")""", "2026-10-22T10:30:00.250")]
    [InlineData("""DateAdd("2026-10-22T23:00:00Z", 2, "hour")""", "2026-10-23T01:00:00Z")]
    [InlineData("""DateAdd("2026-10-22T10:30:00+08:00", 30, "minute")""", "2026-10-22T11:00:00+08:00")]
    [InlineData("""DateAdd("2026-10-22T10:30", 0, "day")""", "2026-10-22T10:30:00")]
    public void DateAdd_KeepsTheDateForm(string call, string expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Theory]
    [InlineData("""DateAdd("2026-10-22", 1.5, "month")""", "DateAdd argument 'amount' must be a whole number for unit 'month'.")]
    [InlineData("""DateAdd("2026-10-22", 1, "days")""", "DateAdd unit 'days' is not supported.")]
    [InlineData("""DateAdd("10/22/2026", 1, "day")""", "DateAdd expects an ISO 8601 date string")]
    [InlineData("""DateAdd("9999-12-31", 1, "day")""", "DateAdd result is outside the supported date range.")]
    public void DateAdd_InvalidArguments_Throw(string call, string message)
    {
        AssertThrows(call, message);
    }

    [Theory]
    [InlineData("""DateDiff("2026-10-22T10:00:00", "2026-10-22T11:30:00", "minute")""", 90d)]
    [InlineData("""DateDiff("2026-10-22T11:30:00", "2026-10-22T10:00:00", "hour")""", -1.5d)]
    [InlineData("""DateDiff("2026-10-01", "2026-10-22", "day")""", 21d)]
    [InlineData("""DateDiff("2026-10-22T00:00:00Z", "2026-10-22T08:00:00+08:00", "second")""", 0d)]
    [InlineData("""DateDiff("2026-10-22T10:00:00", "2026-10-22T10:00:00.250", "millisecond")""", 250d)]
    public void DateDiff_ReturnsEndMinusStart(string call, double expected)
    {
        Assert.Equal((decimal)expected, Evaluate(call));
    }

    [Fact]
    public void DateDiff_PlacesLocalDatesInTheHostTimeZone()
    {
        var local = new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Local);
        var utc = local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(0m, Evaluate($"""DateDiff("2026-10-22T10:30:00", "{utc}", "second")"""));
    }

    [Theory]
    [InlineData("year", 2026)]
    [InlineData("month", 10)]
    [InlineData("day", 22)]
    [InlineData("hour", 10)]
    [InlineData("minute", 30)]
    [InlineData("second", 15)]
    [InlineData("millisecond", 120)]
    [InlineData("dayOfWeek", 4)]
    [InlineData("dayOfYear", 295)]
    public void DatePart_ReadsFields(string part, int expected)
    {
        Assert.Equal(expected, Evaluate($"""DatePart("2026-10-22T10:30:15.120+08:00", "{part}")"""));
    }

    [Theory]
    [InlineData("""FormatDate("2026-10-22T09:05:00", "yyyy/MM/dd HH:mm")""", "2026/10/22 09:05")]
    [InlineData("""FormatDate("2026-10-22T09:05:00Z", "HH:mm K")""", "09:05 Z")]
    [InlineData("""FormatDate("2026-10-22T09:05:00+08:00", "dd MMM yyyy zzz")""", "22 Oct 2026 +08:00")]
    public void FormatDate_UsesInvariantCulture(string call, string expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Theory]
    [InlineData("""ParseDate("2026/10/22 09:05", "yyyy/MM/dd HH:mm")""", "2026-10-22T09:05:00")]
    [InlineData("""ParseDate("22.10.2026 09:05 +08:00", "dd.MM.yyyy HH:mm zzz")""", "2026-10-22T09:05:00+08:00")]
    [InlineData("""ParseDate("2026-10-22T09:05")""", "2026-10-22T09:05:00")]
    [InlineData("""ParseDate("2026-10-22T09:05:00.1234567Z")""", "2026-10-22T09:05:00.123Z")]
    public void ParseDate_ReturnsIsoText(string call, string expected)
    {
        Assert.Equal(expected, Evaluate(call));
    }

    [Theory]
    [InlineData("""ParseDate("2026/10/22", "yyyy-MM-dd")""", "ParseDate could not parse '2026/10/22' with format 'yyyy-MM-dd'.")]
    [InlineData("""ParseDate("2026-13-01")""", "ParseDate expects an ISO 8601 date string")]
    public void ParseDate_InvalidText_Throws(string call, string message)
    {
        AssertThrows(call, message);
    }

    [Fact]
    public void Now_AndToday_ReturnHostLocalIsoText()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var context = new RuleScriptEngine().Execute("""
            var now = Now();
            var utc = UtcNow();
            var today = Today();
            """);
        var after = DateTime.Now.AddSeconds(1);

        var now = context.Get<string>("now")!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{3})?$", now);
        var parsed = DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(parsed, before, after);
        Assert.EndsWith("Z", context.Get<string>("utc"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", context.Get<string>("today")!);
    }

    [Fact]
    public void DateFunctions_AcceptHostDateTimeValues()
    {
        var context = new RuntimeContext();
        context.Set("started", new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Unspecified));
        context.Set("stamp", new DateTimeOffset(2026, 10, 22, 10, 30, 0, TimeSpan.FromHours(8)));

        new RuleScriptEngine().Execute("""
            var due = DateAdd(started, 1, "day");
            var hour = DatePart(stamp, "hour");
            """, context);

        Assert.Equal("2026-10-23T10:30:00", context.Get<string>("due"));
        Assert.Equal(10, context.Get<int>("hour"));
    }

    [Fact]
    public void NewBuiltins_HaveOverloadsForEditors()
    {
        var analysis = new RuleScriptEngine().TryAnalyze("""
            var a = PadLeft("1", 3);
            var b = PadLeft("1", 3, "0");
            var c = PadLeft("1");
            """);

        var padLeft = analysis.Symbols.Functions.Where(function => function.Name == "PadLeft").ToArray();
        Assert.Equal([2, 3], padLeft.Select(function => function.Parameters.Count).Order());
        Assert.All(padLeft, function => Assert.Equal(RuleScriptFunctionKind.Builtin, function.Kind));
        Assert.All(analysis.Diagnostics, value => Assert.Equal(3, value.Line));
        Assert.NotEmpty(analysis.Diagnostics);
    }

    [Fact]
    public void NewBuiltins_DoNotOutrankUntypedUserFunctions()
    {
        const string script = """
            function Sum(values):
                return "user";
            endfunction

            function DateAdd(date, amount, unit):
                return "user";
            endfunction

            var sum = Sum([1, 2]);
            var added = DateAdd("2026-10-22", 1, "day");
            """;
        var engine = new RuleScriptEngine();

        var context = engine.Execute(script);

        Assert.DoesNotContain(engine.TryAnalyze(script).Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Equal("user", context.Get<string>("sum"));
        Assert.Equal("user", context.Get<string>("added"));
    }

    [Fact]
    public void NewBuiltins_DoNotOutrankUntypedHostFunctions()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Now", _ => "host now");
        engine.RegisterFunction("Pow", _ => "host pow");

        var context = engine.Execute("""
            var now = Now();
            var pow = Pow(2, 3);
            """);

        Assert.Equal("host now", context.Get<string>("now"));
        Assert.Equal("host pow", context.Get<string>("pow"));
    }

    [Fact]
    public void NewBuiltins_ValidateArgumentsAtRuntime()
    {
        AssertThrows("""Sum("text")""", "Sum expects an array value.");
        AssertThrows("""Pow("2", 3)""", "Pow expects a number value.");
        AssertThrows("""DateAdd("2026-10-22", 1, 5)""", "DateAdd argument 'unit' must be a string value.");
        AssertThrows("""ObjectMerge({a: 1}, [1])""", "ObjectMerge expects an object value.");
    }

    private static object? Evaluate(string expression)
    {
        return new RuleScriptEngine().Execute($"var result = {expression};").Get("result");
    }

    private static void AssertThrows(string expression, string message)
    {
        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute($"var result = {expression};"));

        Assert.Contains(message, exception.Message);
    }

    private static List<object?> AsList(object? value)
    {
        return Assert.IsAssignableFrom<IList>(value).Cast<object?>().ToList();
    }
}
