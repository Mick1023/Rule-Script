using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class TypedAnalysisTests
{
    [Fact]
    public void Analyze_ReturnsInferredVariableTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            var count = 3;
            var title = "ready";
            var enabled = true;
            var items = [1, 2];
            var payload = JsonParse("{ \"id\": 1 }");
            var empty;
            copied = count;
            """);

        AssertType(result, "count", RuleScriptValueType.Number);
        AssertType(result, "title", RuleScriptValueType.String);
        AssertType(result, "enabled", RuleScriptValueType.Boolean);
        AssertType(result, "items", RuleScriptValueType.Array);
        AssertType(result, "payload", RuleScriptValueType.Object);
        AssertType(result, "empty", RuleScriptValueType.Null);
        AssertType(result, "copied", RuleScriptValueType.Number);
    }

    [Fact]
    public void Analyze_ReturnsFunctionParameterNamesAndTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            function Format(value: number, prefix: string, legacy):
                return prefix + ToString(value);
            endfunction
            """);

        var function = Assert.Single(result.UserFunctions);
        Assert.Equal("Format", function.Name);
        Assert.Collection(
            function.Parameters,
            parameter => Assert.Equal(new RuleScriptParameterSymbol("value", RuleScriptValueType.Number), parameter),
            parameter => Assert.Equal(new RuleScriptParameterSymbol("prefix", RuleScriptValueType.String), parameter),
            parameter => Assert.Equal(new RuleScriptParameterSymbol("legacy", RuleScriptValueType.Unknown), parameter));
    }

    [Fact]
    public void Analyze_ReturnsImportedFunctionParameterNamesAndTypes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rulescript-typed-analysis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "math.rules"),
                "function Add(left: number, right: number): return left + right; endfunction");
            var engine = new RuleScriptEngine { WorkingDirectory = directory };

            var result = engine.Analyze("import \"math.rules\" as math;");
            var function = Assert.Single(result.UserFunctions, value => value.Name == "math.Add");

            Assert.Equal(["left", "right"], function.Parameters.Select(value => value.Name));
            Assert.All(function.Parameters, value => Assert.Equal(RuleScriptValueType.Number, value.Type));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Analyze_WithCursorInsideFunction_ReturnsFunctionVariables()
    {
        var result = new RuleScriptEngine().Analyze("""
            var globalName = "RuleScript";
            function Format(value: number):
                var localText = ToString(value);
                return localText;
            endfunction
            function Other(hidden: bool):
                return hidden;
            endfunction
            """, line: 4, column: 10);

        Assert.Contains("globalName", result.VisibleVariableNames);
        Assert.Contains("value", result.VisibleVariableNames);
        Assert.Contains("localText", result.VisibleVariableNames);
        Assert.DoesNotContain("hidden", result.VisibleVariableNames);
        Assert.Equal(RuleScriptValueType.Number, result.VisibleVariables.Single(value => value.Name == "value").Type);
    }

    [Fact]
    public void TryAnalyze_IncompleteFunctionStillSuggestsFunctionVariables()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Format(value: number):
                var localText = ToString(value);
                return localText;
            """, line: 3, column: 10);

        Assert.False(result.Success);
        Assert.Contains("value", result.Symbols.VisibleVariableNames);
        Assert.Contains("localText", result.Symbols.VisibleVariableNames);
        Assert.DoesNotContain("number", result.Symbols.VisibleVariableNames);
    }

    [Fact]
    public void TypedFunction_AcceptsMatchingArguments()
    {
        var context = new RuleScriptEngine().Execute("""
            function Describe(value: number, label: string, enabled: bool, values: array, payload: object):
                return label + ToString(value);
            endfunction

            result = Describe(42, "Value: ", true, [1], JsonParse("{ \"ok\": true }"));
            """);

        Assert.Equal("Value: 42", context.Get<string>("result"));
    }

    [Fact]
    public void TypedFunction_RejectsUnexpectedArgumentType()
    {
        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("""
            function Double(value: number):
                return value + value;
            endfunction

            result = Double("wrong");
            """));

        Assert.Contains("parameter 'value' expects number", exception.Message);
        Assert.Contains("received string", exception.Message);
    }

    [Fact]
    public void UntypedFunction_RemainsBackwardCompatible()
    {
        var context = new RuleScriptEngine().Execute("""
            function Echo(value):
                return value;
            endfunction

            result = Echo("text");
            """);

        Assert.Equal("text", context.Get<string>("result"));
    }

    [Fact]
    public void TypedFunction_RejectsUnknownTypeName()
    {
        var exception = Assert.Throws<SyntaxException>(() => new RuleScriptEngine().Analyze("""
            function Invalid(value: mystery):
                return value;
            endfunction
            """));

        Assert.Contains("Unknown parameter type 'mystery'", exception.Message);
    }

    private static void AssertType(
        RuleScriptAnalysisResult result,
        string name,
        RuleScriptValueType expected)
    {
        Assert.Equal(expected, result.Variables.Single(variable => variable.Name == name).Type);
    }
}
