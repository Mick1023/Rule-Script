using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140ObjectLiteralTests
{
    [Fact]
    public void Lexer_RecognizesObjectLiteralPunctuation()
    {
        var tokens = new Lexer("{ name: \"Rule\", enabled: true }").Tokenize();

        Assert.Equal(
            [
                TokenType.LeftBrace,
                TokenType.Identifier,
                TokenType.Colon,
                TokenType.String,
                TokenType.Comma,
                TokenType.Identifier,
                TokenType.Colon,
                TokenType.True,
                TokenType.RightBrace,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Parser_CreatesNestedObjectLiteralAst()
    {
        var statement = Assert.IsType<VarStatement>(Assert.Single(Parse("""
            var obj = {
                name: "Rule",
                "child": { age: 18 }
            };
            """)));
        var objectLiteral = Assert.IsType<ObjectLiteralExpression>(statement.Initializer);

        Assert.Equal(["name", "child"], objectLiteral.Properties.Select(property => property.Name));
        var child = Assert.IsType<ObjectLiteralExpression>(objectLiteral.Properties[1].Value);
        Assert.Equal("age", Assert.Single(child.Properties).Name);
    }

    [Fact]
    public void Analyze_InfersObjectAndPropertyTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            var obj = {
                name: "Rule",
                child: { age: 18 },
                items: [{ enabled: true }]
            };
            var name = obj.name;
            var age = obj.child.age;
            var enabled = obj.items[0].enabled;
            """);

        AssertType(result, "obj", RuleScriptValueType.Object);
        AssertType(result, "name", RuleScriptValueType.String);
        AssertType(result, "age", RuleScriptValueType.Number);
        AssertType(result, "enabled", RuleScriptValueType.Boolean);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_MissingPropertyReturnsCodedDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = { name: "Rule" };
            var value = obj.missing;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.PropertyNotFound);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("missing", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_DuplicatePropertyReturnsCodedDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("var obj = { name: \"first\", name: \"second\" };");

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.DuplicateObjectProperty);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("name", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_TraversesObjectPropertyValues()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = {
                missing: missingVariable,
                invalid: 1 + true
            };
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.UndefinedVariable
            && value.TokenText == "missingVariable");
        Assert.Contains(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.TypeMismatch
            && value.TokenText == "+");
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_UnknownObjectShapeDoesNotReportMissingProperty()
    {
        var engine = new RuleScriptEngine();
        engine.SetKnownVariable("external", RuleScriptValueType.Object);

        var result = engine.TryAnalyze("var value = external.hostDefinedProperty;");

        Assert.DoesNotContain(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.PropertyNotFound);
    }

    [Fact]
    public void Execute_CreatesObjectAndReadsNestedProperties()
    {
        var context = new RuleScriptEngine().Execute("""
            var obj = {
                name: "Rule",
                age: 18,
                enabled: true,
                child: { name: "Nested" }
            };
            name = obj.name;
            age = obj.age;
            enabled = obj.enabled;
            childName = obj.child.name;
            """);

        Assert.Equal("Rule", context.Get<string>("name"));
        Assert.Equal(18d, context.Get<double>("age"));
        Assert.True(context.Get<bool>("enabled"));
        Assert.Equal("Nested", context.Get<string>("childName"));
    }

    [Fact]
    public void Execute_SupportsEmptyObjectAndQuotedPropertyName()
    {
        var context = new RuleScriptEngine().Execute("""
            var empty = {};
            var obj = { "displayName": "Rule Script" };
            value = obj.displayName;
            """);

        var empty = context.Get<Dictionary<string, object?>>("empty");
        Assert.NotNull(empty);
        Assert.Empty(empty);
        Assert.Equal("Rule Script", context.Get<string>("value"));
    }

    [Fact]
    public async Task ExecuteAsync_SupportsObjectLiteral()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            var obj = { child: { value: 42 } };
            result = obj.child.value;
            """);

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_DuplicatePropertyThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("var obj = { name: 1, name: 2 };"));

        Assert.Contains("declared more than once", exception.Message, StringComparison.Ordinal);
        Assert.Equal("name", exception.TokenText);
    }

    [Fact]
    public void Execute_DuplicatePropertyDoesNotEvaluateDuplicateValue()
    {
        var calls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ =>
        {
            calls++;
            return 2;
        });

        Assert.Throws<RuntimeException>(() =>
            engine.Execute("var obj = { value: 1, value: Read() };"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ExistingDictionaryPropertyAccessStillWorks()
    {
        var context = new RuntimeContext();
        context.Set("external", new Dictionary<string, object?>
        {
            ["name"] = "Existing"
        });

        new RuleScriptEngine().Execute("result = external.name;", context);

        Assert.Equal("Existing", context.Get<string>("result"));
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private static void AssertType(
        RuleScriptAnalysisResult result,
        string name,
        RuleScriptValueType expected)
    {
        var symbol = Assert.Single(result.Variables, variable => variable.Name == name);
        Assert.Equal(expected, symbol.Type);
    }
}
