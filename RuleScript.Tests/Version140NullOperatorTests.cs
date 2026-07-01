using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140NullOperatorTests
{
    [Fact]
    public void Lexer_RecognizesNullOperators()
    {
        var tokens = new Lexer("value?.child ?? fallback").Tokenize();

        Assert.Equal(
            [
                TokenType.Identifier,
                TokenType.QuestionDot,
                TokenType.Identifier,
                TokenType.QuestionQuestion,
                TokenType.Identifier,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Parser_NullCoalescingIsRightAssociative()
    {
        var statement = Assert.IsType<VarStatement>(Assert.Single(Parse("var result = a ?? b ?? c;")));
        var outer = Assert.IsType<NullCoalescingExpression>(statement.Initializer);

        Assert.Equal("a", Assert.IsType<IdentifierExpression>(outer.Left).Name);
        var right = Assert.IsType<NullCoalescingExpression>(outer.Right);
        Assert.Equal("b", Assert.IsType<IdentifierExpression>(right.Left).Name);
        Assert.Equal("c", Assert.IsType<IdentifierExpression>(right.Right).Name);
    }

    [Fact]
    public void Parser_NullCoalescingHasLowerPrecedenceThanOr()
    {
        var statement = Assert.IsType<VarStatement>(Assert.Single(Parse("var result = a or b ?? c;")));
        var expression = Assert.IsType<NullCoalescingExpression>(statement.Initializer);

        Assert.Equal(TokenType.Or, Assert.IsType<BinaryExpression>(expression.Left).Operator);
        Assert.Equal("c", Assert.IsType<IdentifierExpression>(expression.Right).Name);
    }

    [Fact]
    public void Parser_CreatesChainedConditionalMemberAccess()
    {
        var statement = Assert.IsType<VarStatement>(Assert.Single(Parse("var name = obj?.child?.name;")));
        var name = Assert.IsType<ConditionalMemberAccessExpression>(statement.Initializer);
        var child = Assert.IsType<ConditionalMemberAccessExpression>(name.Target);

        Assert.Equal("name", name.MemberName);
        Assert.Equal("child", child.MemberName);
        Assert.Equal("obj", Assert.IsType<IdentifierExpression>(child.Target).Name);
    }

    [Fact]
    public void Analyze_InfersConditionalPropertyAndCoalescedTypes()
    {
        var result = new RuleScriptEngine().Analyze("""
            var obj = { child: { name: "Rule" } };
            var optionalName = obj?.child?.name;
            var name = optionalName ?? "fallback";
            """);

        AssertType(result, "optionalName", RuleScriptValueType.String);
        AssertType(result, "name", RuleScriptValueType.String);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_DirectNullAccessReturnsCodedDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = null;
            var name = obj.name;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.NullAccess);
        Assert.Equal("name", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_DirectNullableAccessReturnsCodedDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var root = { child: { name: "Rule" } };
            var child = root?.child;
            var name = child.name;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.NullAccess);
        Assert.Contains("object?", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_ConditionalMissingPropertyReturnsPropertyNotFound()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = { name: "Rule" };
            var value = obj?.missing;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.PropertyNotFound);
        Assert.Equal("missing", diagnostic.TokenText);
    }

    [Theory]
    [InlineData("var value = \"known\" ?? \"fallback\";")]
    [InlineData("var obj = { name: \"Rule\" }; var value = obj?.name ?? 42;")]
    public void Analyze_InvalidNullCoalescingReturnsCodedDiagnostic(string script)
    {
        var result = new RuleScriptEngine().TryAnalyze(script);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.InvalidNullCoalescing);
        Assert.Equal("??", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_NullCoalescingWithFallbackIsValid()
    {
        var result = new RuleScriptEngine().TryAnalyze("var value = null ?? \"fallback\";");

        Assert.True(result.Success);
        AssertType(result.Symbols, "value", RuleScriptValueType.String);
    }

    [Fact]
    public void Execute_NullCoalescingUsesFallbackForNull()
    {
        var context = new RuleScriptEngine().Execute("result = null ?? \"fallback\";");

        Assert.Equal("fallback", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_NullCoalescingShortCircuitsRightOperand()
    {
        var calls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Fallback", _ =>
        {
            calls++;
            return "fallback";
        });

        var context = engine.Execute("result = \"known\" ?? Fallback();");

        Assert.Equal("known", context.Get<string>("result"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Execute_ChainedConditionalAccessReturnsNullWithoutThrowing()
    {
        var context = new RuleScriptEngine().Execute("""
            var obj = { child: null };
            result = obj?.child?.name;
            fallback = obj?.child?.name ?? "missing";
            """);

        Assert.Null(context.Get<object>("result"));
        Assert.Equal("missing", context.Get<string>("fallback"));
    }

    [Fact]
    public void Execute_ChainedConditionalAccessReadsNestedProperty()
    {
        var context = new RuleScriptEngine().Execute("""
            var obj = { child: { name: "Nested" } };
            result = obj?.child?.name;
            """);

        Assert.Equal("Nested", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ConditionalReceiverIsEvaluatedOnce()
    {
        var calls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ =>
        {
            calls++;
            return new Dictionary<string, object?> { ["name"] = "Rule" };
        });

        var context = engine.Execute("result = Read()?.name;");

        Assert.Equal("Rule", context.Get<string>("result"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_SupportsNullOperators()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            var obj = null;
            result = obj?.child?.name ?? "async fallback";
            """);

        Assert.Equal("async fallback", context.Get<string>("result"));
    }

    [Fact]
    public void ExistingDirectNullMemberAccessStillThrows()
    {
        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("var obj = null; result = obj.name;"));

        Assert.Contains("on null", exception.Message, StringComparison.Ordinal);
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
