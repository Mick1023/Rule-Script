using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class EditorToleranceTests
{
    [Fact]
    public void Tokenize_StrictMode_StillThrowsForUnsupportedCharacter()
    {
        Assert.Throws<SyntaxException>(() => new Lexer("var a = 1 $ 2;").Tokenize());
    }

    [Fact]
    public void TokenizeWithDiagnostics_SkipsUnsupportedCharacterAndContinues()
    {
        var diagnostics = new List<SyntaxException>();

        var tokens = new Lexer("var a = $1;\nvar b = 2;").TokenizeWithDiagnostics(diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(9, diagnostic.Column);
        Assert.Contains(tokens, token => token.Type == TokenType.Identifier && token.Lexeme == "b" && token.Line == 2);
        Assert.Equal(TokenType.EndOfFile, tokens[^1].Type);
    }

    [Fact]
    public void TokenizeWithDiagnostics_RecoversUnterminatedStringAndBadEscape()
    {
        var diagnostics = new List<SyntaxException>();

        var tokens = new Lexer("var a = \"x\\qy\nvar b = 2;").TokenizeWithDiagnostics(diagnostics);

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("Unsupported string escape", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("Unterminated string literal", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Type == TokenType.String && Equals(token.Literal, "xqy"));
        Assert.Contains(tokens, token => token.Type == TokenType.Identifier && token.Lexeme == "b");
    }

    [Fact]
    public void TokenizeWithDiagnostics_RecoversUnterminatedMultiLineComment()
    {
        var diagnostics = new List<SyntaxException>();

        var tokens = new Lexer("var a = 1;\n/* open\ncomment").TokenizeWithDiagnostics(diagnostics, includeComments: true);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(2, diagnostic.Line);
        Assert.Contains(tokens, token => token.Type == TokenType.MultiLineComment);
    }

    [Fact]
    public void MultiLineCommentToken_ReportsStartAndEndPositions()
    {
        var tokens = new Lexer("var a = 1;\n  /* first\n  second */ var b = 2;").Tokenize(includeComments: true);

        var comment = Assert.Single(tokens, token => token.Type == TokenType.MultiLineComment);
        Assert.Equal(2, comment.Line);
        Assert.Equal(3, comment.Column);
        Assert.Equal(3, comment.EndLine);
        Assert.Equal(12, comment.EndColumn);

        var b = Assert.Single(tokens, token => token.Lexeme == "b");
        Assert.Equal(3, b.Line);
    }

    [Fact]
    public void SingleLineToken_EndPositionMatchesLexemeLength()
    {
        var token = new Lexer("  value").Tokenize()[0];

        Assert.Equal(1, token.EndLine);
        Assert.Equal(8, token.EndColumn);
    }

    [Fact]
    public void AnalyzeDocument_StrictMode_StillThrowsForSyntaxErrors()
    {
        Assert.Throws<SyntaxException>(() => RuleScriptLanguageService.AnalyzeDocument("var a = ;"));
    }

    [Fact]
    public void TryAnalyzeDocument_KeepsNavigationWorkingWhileDocumentIsIncomplete()
    {
        const string source = """
            function AddOne(value: number):
                return value + 1;
            endfunction

            var broken = ;
            var total = AddOne(1);
            """;

        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.False(document.Success);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Severity == RuleScriptDiagnosticSeverity.Error && diagnostic.Line == 5);
        Assert.Contains(document.Analysis.Functions, function => function.Name == "AddOne");

        var definition = RuleScriptLanguageService.GetDefinition(document, 6, 13);
        Assert.NotNull(definition);
        Assert.Equal("AddOne", definition.Name);

        var references = RuleScriptLanguageService.FindReferences(document, 1, 10);
        Assert.Equal(2, references.Count);
    }

    [Fact]
    public void TryAnalyzeDocument_CollectsLexicalAndParserDiagnosticsInSourceOrder()
    {
        const string source = """
            var a = ;
            var b = 1 $;
            var c = 3;
            """;

        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.False(document.Success);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Message.Contains("Unsupported character", StringComparison.Ordinal));
        var lines = document.Diagnostics.Select(diagnostic => diagnostic.Line ?? 0).ToArray();
        Assert.Equal(lines.Order().ToArray(), lines);
        Assert.Contains(document.Analysis.VariableNames, name => name == "c");
    }

    [Fact]
    public void TryAnalyzeDocument_ValidSource_Succeeds()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var a = 1;\nresult = a + 1;");

        Assert.True(document.Success);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity == RuleScriptDiagnosticSeverity.Error);
    }

    [Fact]
    public void ParsedExpressions_ExposeSourceRanges()
    {
        var document = RuleScriptLanguageService.AnalyzeDocument("var total = price * (count + 1);\nPrint(total);");

        var declaration = Assert.IsType<VarStatement>(document.Statements[0]);
        var initializer = Assert.IsType<BinaryExpression>(declaration.Initializer);
        Assert.Equal(new RuleScriptSourceRange(null, 1, 13, 1, 32), initializer.Range);
        Assert.Equal(new RuleScriptSourceRange(null, 1, 13, 1, 18), initializer.Left.Range);
        Assert.Equal(new RuleScriptSourceRange(null, 1, 22, 1, 31), initializer.Right.Range);
        Assert.Equal(new RuleScriptSourceRange(null, 1, 1, 1, 33), declaration.Range);

        var call = Assert.IsType<FunctionCallExpression>(Assert.IsType<ExpressionStatement>(document.Statements[1]).Expression);
        Assert.Equal(new RuleScriptSourceRange(null, 2, 1, 2, 13), call.Range);
    }

    [Fact]
    public void AnalysisCache_GetOrTryAnalyze_DoesNotThrowAndStrictLookupStillValidates()
    {
        var cache = new RuleScriptAnalysisCache();
        const string broken = "var a = ;";

        var tolerant = cache.GetOrTryAnalyze("doc", broken);

        Assert.False(tolerant.Success);
        Assert.Same(tolerant, cache.GetOrTryAnalyze("doc", broken));
        Assert.Throws<SyntaxException>(() => cache.GetOrAnalyze("doc", broken));
    }
}
