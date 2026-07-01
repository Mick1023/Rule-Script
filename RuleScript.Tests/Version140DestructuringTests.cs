using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140DestructuringTests
{
    [Fact]
    public void Parser_CreatesArrayDestructuringAst()
    {
        var statement = Assert.IsType<DestructuringVarStatement>(Assert.Single(Parse("var [first, second] = values;")));
        var pattern = Assert.IsType<ArrayDestructuringPattern>(statement.Pattern);

        Assert.Equal(["first", "second"], pattern.Names);
        Assert.IsType<IdentifierExpression>(statement.Initializer);
    }

    [Fact]
    public void Parser_CreatesObjectDestructuringAst()
    {
        var statement = Assert.IsType<DestructuringVarStatement>(Assert.Single(Parse("var { name, age } = person;")));
        var pattern = Assert.IsType<ObjectDestructuringPattern>(statement.Pattern);

        Assert.Equal(["name", "age"], pattern.Names);
    }

    [Fact]
    public void Parser_DestructuringRequiresInitializer()
    {
        Assert.Throws<SyntaxException>(() => Parse("var [first, second];"));
    }

    [Fact]
    public void Execute_ArrayDestructuringAssignsElements()
    {
        var context = Execute("""
            var values = ["A", 18, true];
            var [name, age] = values;
            result = name + ToString(age);
            """);

        Assert.Equal("A18", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ObjectDestructuringAssignsProperties()
    {
        var context = Execute("""
            var person = { name: "Rule", age: 18, enabled: true };
            var { name, age } = person;
            result = name + ToString(age);
            """);

        Assert.Equal("Rule18", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ObjectDestructuringSupportsHostObjects()
    {
        var context = new RuntimeContext();
        context.Set("person", new Person("Rule", 18));

        new RuleScriptEngine().Execute("var { Name, Age } = person; result = Name + ToString(Age);", context);

        Assert.Equal("Rule18", context.Get<string>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_DestructuringUsesSameSemantics()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("var [first, second] = [1, 2]; result = first + second;");

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void Analyze_InfersIndividualArrayAndObjectBindingTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            var [name, age, enabled] = ["Rule", 18, true];
            var { title, count } = { title: "Script", count: 2 };
            """);

        AssertType(result, "name", RuleScriptValueType.String);
        AssertType(result, "age", RuleScriptValueType.Number);
        AssertType(result, "enabled", RuleScriptValueType.Boolean);
        AssertType(result, "title", RuleScriptValueType.String);
        AssertType(result, "count", RuleScriptValueType.Number);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_DestructuringNamesAreAvailableForCompletion()
    {
        var result = new RuleScriptEngine().Analyze("var [first, second] = [1, 2];");

        Assert.Contains("first", result.VariableNames);
        Assert.Contains("second", result.VariableNames);
    }

    [Theory]
    [InlineData("var [value] = { value: 1 };")]
    [InlineData("var { value } = [1];")]
    public void Analyze_InvalidInitializerTypeProducesDiagnostic(string script)
    {
        var result = new RuleScriptEngine().TryAnalyze(script);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void Analyze_MissingArrayElementProducesInvalidAssignmentDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("var [first, second] = [1];");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.InvalidAssignment);
    }

    [Fact]
    public void Analyze_MissingObjectPropertyProducesPropertyDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("var { name, age } = { name: \"Rule\" }; ");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.PropertyNotFound);
        Assert.Equal("age", diagnostic.TokenText);
    }

    [Fact]
    public void Analyze_DuplicateBindingProducesDuplicateDeclarationDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("var [value, value] = [1, 2];");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.DuplicateDeclaration);
    }

    [Fact]
    public void Execute_MissingBindingValuesFailStrictly()
    {
        Assert.Throws<RuntimeException>(() => Execute("var [first, second] = [1];"));
        Assert.Throws<RuntimeException>(() => Execute("var { name, age } = { name: \"Rule\" };"));
    }

    [Fact]
    public void ExistingVariableDeclarationRemainsUnchanged()
    {
        var context = Execute("var value = 42; result = value;");

        Assert.Equal(42d, context.Get<double>("result"));
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

    private sealed record Person(string Name, int Age);
}
