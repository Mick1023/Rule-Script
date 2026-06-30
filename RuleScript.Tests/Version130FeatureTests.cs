using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version130FeatureTests
{
    [Fact]
    public void Lexer_RecognizesSwitchKeywords()
    {
        var tokens = new Lexer("switch value: case 1 when true: default: endswitch").Tokenize();

        Assert.Equal(
            [
                TokenType.Switch,
                TokenType.Identifier,
                TokenType.Colon,
                TokenType.Case,
                TokenType.Number,
                TokenType.When,
                TokenType.True,
                TokenType.Colon,
                TokenType.Default,
                TokenType.Colon,
                TokenType.EndSwitch,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Lexer_RecognizesNullLiteral()
    {
        var tokens = new Lexer("null").Tokenize();

        Assert.Equal(TokenType.Null, tokens[0].Type);
    }

    [Fact]
    public void Parser_GroupsConsecutiveCaseLabelsAndAcceptsGenericEnd()
    {
        var statement = Assert.IsType<SwitchStatement>(Assert.Single(Parse("""
            switch value:
                case "A":
                case "B":
                    result = "known";
                default:
                    result = "unknown";
            end
            """)));

        var switchCase = Assert.Single(statement.Cases);
        Assert.Equal(2, switchCase.Labels.Count);
        Assert.Single(switchCase.Body);
        Assert.NotNull(statement.DefaultBranch);
        Assert.Single(statement.DefaultBranch!);
    }

    [Fact]
    public void Parser_CommaSeparatedLabelsShareBody()
    {
        var statement = Assert.IsType<SwitchStatement>(Assert.Single(Parse("""
            switch value:
                case "A", "B", "C":
                    result = "known";
            endswitch
            """)));

        var switchCase = Assert.Single(statement.Cases);
        Assert.Equal(3, switchCase.Labels.Count);
        Assert.Single(switchCase.Body);
    }

    [Fact]
    public void Parser_CaseGuardIsStoredOnEveryCommaSeparatedLabel()
    {
        var statement = Assert.IsType<SwitchStatement>(Assert.Single(Parse("""
            switch value:
                case "A", "B" when enabled:
                    result = "known";
            endswitch
            """)));

        var switchCase = Assert.Single(statement.Cases);
        Assert.All(switchCase.Labels, label => Assert.IsType<IdentifierExpression>(label.Guard));
    }

    [Theory]
    [InlineData("switch value: endswitch", "at least one case or default")]
    [InlineData("switch value: default: case 1: endswitch", "default branch must be the last")]
    [InlineData("switch value: default: default: endswitch", "only one default")]
    public void Parser_InvalidSwitchShape_ThrowsUsefulError(string script, string expectedMessage)
    {
        var exception = Assert.Throws<SyntaxException>(() => Parse(script));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_FirstMatchingCaseRunsWithoutFallthrough()
    {
        var context = new RuleScriptEngine().Execute("""
            var value = 2;
            var result = "unset";
            switch value:
                case 1:
                    result = "one";
                case 2:
                    result = "two";
                case 3:
                    result = "three";
                default:
                    result = "default";
            endswitch
            """);

        Assert.Equal("two", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ConsecutiveLabelsShareBodyAndDefaultHandlesNoMatch()
    {
        var engine = new RuleScriptEngine();

        var grouped = engine.Execute("""
            switch "B":
                case "A":
                case "B":
                    result = "known";
                default:
                    result = "unknown";
            endswitch
            """);
        var unmatched = engine.Execute("""
            switch "C":
                case "A":
                case "B":
                    result = "known";
                default:
                    result = "unknown";
            endswitch
            """);

        Assert.Equal("known", grouped.Get<string>("result"));
        Assert.Equal("unknown", unmatched.Get<string>("result"));
    }

    [Fact]
    public void Execute_CommaSeparatedLabelsAndNullLiteralAreSupported()
    {
        var context = new RuleScriptEngine().Execute("""
            var value = null;
            switch value:
                case "A", "B":
                    result = "text";
                case null:
                    result = "missing";
                default:
                    result = "other";
            endswitch
            """);

        Assert.Equal("missing", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_SwitchExpressionIsEvaluatedOnce()
    {
        var calls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ =>
        {
            calls++;
            return 2;
        });

        var context = engine.Execute("""
            switch Read():
                case 1:
                    result = "one";
                case 2:
                    result = "two";
            endswitch
            """);

        Assert.Equal(1, calls);
        Assert.Equal("two", context.Get<string>("result"));
    }

    [Theory]
    [InlineData(2, "Inspect")]
    [InlineData(4, "Escalate")]
    public void Execute_WhenGuardSelectsFirstEligibleCase(int retryCount, string expected)
    {
        var context = new RuntimeContext();
        context.Set("retryCount", retryCount);

        new RuleScriptEngine().Execute("""
            switch "warning":
                case "warning" when retryCount > 3:
                    result = "Escalate";
                case "warning":
                    result = "Inspect";
                default:
                    result = "Unknown";
            endswitch
            """, context);

        Assert.Equal(expected, context.Get<string>("result"));
    }

    [Fact]
    public void Execute_NonBooleanGuardThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("""
            switch 1:
                case 1 when "yes":
                    result = "invalid";
                default:
                    result = "other";
            endswitch
            """));

        Assert.Contains("guard must evaluate to a bool", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GuardIsNotEvaluatedForUnmatchedLabel()
    {
        var guardCalls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Check", _ =>
        {
            guardCalls++;
            return true;
        });

        var context = engine.Execute("""
            switch 2:
                case 1 when Check():
                    result = "one";
                case 2:
                    result = "two";
                default:
                    result = "other";
            endswitch
            """);

        Assert.Equal(0, guardCalls);
        Assert.Equal("two", context.Get<string>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_SupportsSwitch()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            switch 42:
                case 42:
                    result = "matched";
                default:
                    result = "unmatched";
            endswitch
            """);

        Assert.Equal("matched", context.Get<string>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_SupportsWhenGuard()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            switch 42:
                case 42 when true:
                    result = "guarded";
                default:
                    result = "unmatched";
            endswitch
            """);

        Assert.Equal("guarded", context.Get<string>("result"));
    }

    [Fact]
    public void Analyze_TraversesCaseLabelsAndAllBranches()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch missingLabel:
                case missingCase:
                    var fromCase = 1;
                default:
                    var fromDefault = 2;
            endswitch
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TokenText == "missingLabel");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TokenText == "missingCase");
        Assert.Contains("fromCase", result.Symbols.VariableNames);
        Assert.Contains("fromDefault", result.Symbols.VariableNames);
    }

    [Fact]
    public void Analyze_DuplicateConstantCaseReturnsCodedError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch 1:
                case 1, 1:
                    result = "duplicate";
                default:
                    result = "other";
            endswitch
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.DuplicateCase);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_IncompatibleCaseTypeReturnsTypeMismatch()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch "status":
                case 1:
                    result = "invalid";
                default:
                    result = "other";
            endswitch
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.TypeMismatch
            && value.Message.Contains("case label", StringComparison.Ordinal));
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_MissingDefaultReturnsWarning()
    {
        var result = new RuleScriptEngine().TryAnalyze("switch 1: case 1: result = \"one\"; endswitch");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.MissingDefaultBranch);
        Assert.Equal(RuleScriptDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(result.Success);
    }

    [Fact]
    public void Analyze_NonBooleanGuardReturnsTypeMismatch()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch 1:
                case 1 when "yes":
                    result = "invalid";
                default:
                    result = "other";
            endswitch
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.TypeMismatch
            && value.Message.Contains("guard", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Analyze_GuardedDuplicateWithUnguardedFallbackIsAllowed()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch 1:
                case 1 when enabled:
                    result = "guarded";
                case 1:
                    result = "fallback";
                default:
                    result = "other";
            endswitch
            """);

        Assert.DoesNotContain(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.DuplicateCase);
        Assert.Contains(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.UndefinedVariable
            && value.TokenText == "enabled");
    }

    [Fact]
    public void Analyze_SharedCommaLabelGuardIsAnalyzedOnce()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            switch 1:
                case 1, 2 when missingGuard:
                    result = "guarded";
                default:
                    result = "other";
            endswitch
            """);

        Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.UndefinedVariable
            && value.TokenText == "missingGuard");
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }
}
