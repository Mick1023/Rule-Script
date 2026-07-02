using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140CollectionModificationTests
{
    [Fact]
    public void Parser_CollectionFunctionCallUsesExistingCallAst()
    {
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(Parse("ArrayInsert(items, 0, value);")));
        var call = Assert.IsType<FunctionCallExpression>(statement.Expression);

        Assert.Equal("ArrayInsert", call.Name);
        Assert.Equal(3, call.Arguments.Count);
    }

    [Fact]
    public void ArrayInsert_InsertsAtIndexAndReturnsSameArray()
    {
        var context = Execute("""
            var items = [1, 3];
            var returned = ArrayInsert(items, 1, 2);
            first = returned[0];
            middle = items[1];
            last = returned[2];
            """);

        Assert.Equal(1d, context.Get<double>("first"));
        Assert.Equal(2d, context.Get<double>("middle"));
        Assert.Equal(3d, context.Get<double>("last"));
    }

    [Fact]
    public void ArrayRemoveAt_RemovesAndReturnsItem()
    {
        var context = Execute("""
            var items = ["A", "B", "C"];
            removed = ArrayRemoveAt(items, 1);
            remaining = Join("", items);
            """);

        Assert.Equal("B", context.Get<string>("removed"));
        Assert.Equal("AC", context.Get<string>("remaining"));
    }

    [Theory]
    [InlineData("[3, 1, 2]", "123")]
    [InlineData("[\"c\", \"a\", \"b\"]", "abc")]
    public void ArraySort_SortsSupportedHomogeneousArrays(string literal, string expected)
    {
        var context = Execute($"""
            var items = {literal};
            var returned = ArraySort(items);
            result = Join("", returned);
            """);

        Assert.Equal(expected, context.Get<string>("result"));
    }

    [Fact]
    public void ArraySort_RejectsMixedElementTypes()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("ArraySort([1, \"A\"]);"));

        Assert.Contains("all elements", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectFunctions_ReturnSortedKeysAndMembership()
    {
        var context = Execute("""
            var obj = { z: 1, name: "Rule", enabled: true };
            keys = Join(",", ObjectKeys(obj));
            contains = ObjectContainsKey(obj, "name");
            missing = ObjectContainsKey(obj, "age");
            """);

        Assert.Equal("enabled,name,z", context.Get<string>("keys"));
        Assert.True(context.Get<bool>("contains"));
        Assert.False(context.Get<bool>("missing"));
    }

    [Fact]
    public void Analyze_InfersCollectionFunctionReturnTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            var values = ["A", "B"];
            var inserted = ArrayInsert(values, 1, "X");
            var removed = ArrayRemoveAt(values, 0);
            var sorted = ArraySort(values);
            var keys = ObjectKeys({ name: "Rule" });
            var contains = ObjectContainsKey({ name: "Rule" }, "name");
            """);

        AssertType(result, "inserted", RuleScriptValueType.Array);
        AssertType(result, "removed", RuleScriptValueType.String);
        AssertType(result, "sorted", RuleScriptValueType.Array);
        AssertType(result, "keys", RuleScriptValueType.Array);
        AssertType(result, "contains", RuleScriptValueType.Boolean);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_ExposesTypedBuiltinSignaturesForCompletionAndSignatureHelp()
    {
        var result = new RuleScriptEngine().Analyze(string.Empty);
        var insert = Assert.Single(result.BuiltinFunctions, function => function.Name == "ArrayInsert");
        var objectKeys = Assert.Single(result.BuiltinFunctions, function => function.Name == "ObjectKeys");
        var toString = Assert.Single(result.BuiltinFunctions, function => function.Name == "ToString");

        Assert.Equal(result.BuiltinFunctionNames, result.BuiltinFunctions.Select(function => function.Name));
        Assert.Equal(["array", "index", "value"], insert.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(RuleScriptValueType.Array, insert.ReturnType);
        Assert.Equal(RuleScriptValueType.Array, objectKeys.ReturnType);
        Assert.Equal(["value"], toString.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(RuleScriptValueType.String, toString.ReturnType);
    }

    [Fact]
    public void Analyze_InvalidCollectionArgumentProducesTypeDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("ArrayRemoveAt(\"not-array\", 0);");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("expects array", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArrayRemove_ExistingBehaviorRemainsAvailable()
    {
        var context = Execute("""
            var items = [1, 2, 3];
            removed = ArrayRemove(items, 2);
            result = Join("", items);
            """);

        Assert.True(context.Get<bool>("removed"));
        Assert.Equal("13", context.Get<string>("result"));
    }

    [Fact]
    public void CollectionFunctions_ReportInvalidIndexes()
    {
        Assert.Throws<RuntimeException>(() => Execute("ArrayInsert([1], 2, 3);"));
        Assert.Throws<RuntimeException>(() => Execute("ArrayRemoveAt([1], -1);"));
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }

    private static void AssertType(RuleScriptAnalysisResult result, string name, RuleScriptValueType type)
    {
        Assert.Equal(type, Assert.Single(result.Variables, variable => variable.Name == name).Type);
    }
}
