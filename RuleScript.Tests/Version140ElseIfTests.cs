using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140ElseIfTests
{
    [Fact]
    public void Lexer_RecognizesElseIfKeyword()
    {
        var tokens = new Lexer("if true then: elseif false then: else: endif").Tokenize();

        Assert.Equal(
            [
                TokenType.If,
                TokenType.True,
                TokenType.Then,
                TokenType.Colon,
                TokenType.ElseIf,
                TokenType.False,
                TokenType.Then,
                TokenType.Colon,
                TokenType.Else,
                TokenType.Colon,
                TokenType.EndIf,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Parser_LowersElseIfClausesToNestedIfStatements()
    {
        var outer = Assert.IsType<IfStatement>(Assert.Single(Parse("""
            if first then:
                result = "first";
            elseif second then:
                result = "second";
            elseif third then:
                result = "third";
            else:
                result = "fallback";
            endif
            """)));

        var second = Assert.IsType<IfStatement>(Assert.Single(outer.ElseBranch));
        var third = Assert.IsType<IfStatement>(Assert.Single(second.ElseBranch));

        Assert.Equal("first", Assert.IsType<IdentifierExpression>(outer.Condition).Name);
        Assert.Equal("second", Assert.IsType<IdentifierExpression>(second.Condition).Name);
        Assert.Equal("third", Assert.IsType<IdentifierExpression>(third.Condition).Name);
        Assert.Single(outer.ThenBranch);
        Assert.Single(second.ThenBranch);
        Assert.Single(third.ThenBranch);
        Assert.Single(third.ElseBranch);
    }

    [Fact]
    public void Analyze_CollectsSymbolsFromEveryElseIfBranch()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            if true then:
                var fromIf = 1;
            elseif false then:
                var fromElseIf = 2;
            else:
                var fromElse = 3;
            endif
            """);

        Assert.True(result.Success);
        Assert.Contains("fromIf", result.Symbols.VariableNames);
        Assert.Contains("fromElseIf", result.Symbols.VariableNames);
        Assert.Contains("fromElse", result.Symbols.VariableNames);
    }

    [Fact]
    public void Analyze_NonBooleanElseIfConditionReturnsTypeMismatch()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            if false then:
                result = "if";
            elseif "not boolean" then:
                result = "elseif";
            endif
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.TypeMismatch
            && value.Message.Contains("If condition", StringComparison.Ordinal));
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(true, true, "if")]
    [InlineData(false, true, "elseif")]
    [InlineData(false, false, "else")]
    public void Execute_SelectsFirstMatchingBranch(bool first, bool second, string expected)
    {
        var context = new RuntimeContext();
        context.Set("first", first);
        context.Set("second", second);

        new RuleScriptEngine().Execute("""
            if first then:
                result = "if";
            elseif second then:
                result = "elseif";
            else:
                result = "else";
            endif
            """, context);

        Assert.Equal(expected, context.Get<string>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_SupportsElseIf()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            if false then:
                result = "if";
            elseif true then:
                result = "elseif";
            else:
                result = "else";
            endif
            """);

        Assert.Equal("elseif", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_DoesNotEvaluateElseIfAfterEarlierMatch()
    {
        var elseIfCalls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("CheckElseIf", _ =>
        {
            elseIfCalls++;
            return true;
        });

        var context = engine.Execute("""
            if true then:
                result = "if";
            elseif CheckElseIf() then:
                result = "elseif";
            endif
            """);

        Assert.Equal("if", context.Get<string>("result"));
        Assert.Equal(0, elseIfCalls);
    }

    [Fact]
    public void Parser_ElseIfChainUsesSingleEndIf()
    {
        var statements = Parse("""
            if false then:
                result = 1;
            elseif true then:
                result = 2;
            endif
            var after = 3;
            """);

        Assert.Collection(
            statements,
            statement => Assert.IsType<IfStatement>(statement),
            statement => Assert.IsType<VarStatement>(statement));
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }
}
