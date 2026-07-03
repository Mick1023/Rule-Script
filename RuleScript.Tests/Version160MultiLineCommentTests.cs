using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160MultiLineCommentTests
{
    [Fact]
    public void Lexer_SkipsSingleLineBlockComment()
    {
        var tokens = new Lexer("var /* comment */ value = 1;").Tokenize();

        Assert.Equal(
            [
                TokenType.Var,
                TokenType.Identifier,
                TokenType.Assign,
                TokenType.Number,
                TokenType.Semicolon,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Parser_IgnoresBlockCommentAcrossLines()
    {
        const string source = """
            var value = 1;
            /* This comment
               spans multiple lines. */
            value = value + 1;
            """;

        var statements = new Parser(new Lexer(source).Tokenize()).Parse();

        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void Lexer_BlockCommentMayContainPunctuationAndKeywords()
    {
        const string source = """
            /*
            if condition then:
                return value;
            endif
            */
            var value = 1;
            """;

        var tokens = new Lexer(source).Tokenize();

        Assert.Equal(
            [
                TokenType.Var,
                TokenType.Identifier,
                TokenType.Assign,
                TokenType.Number,
                TokenType.Semicolon,
                TokenType.EndOfFile
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Lexer_BlockCommentMarkersInsideStringRemainStringContent()
    {
        var tokens = new Lexer("var text = \"/* not comment */\";").Tokenize();

        Assert.Equal(TokenType.String, tokens[3].Type);
        Assert.Equal("/* not comment */", tokens[3].Literal);
    }

    [Fact]
    public void Lexer_UnterminatedBlockComment_ReportsDiagnosticAtOpeningDelimiter()
    {
        const string source = """
            var value = 1;
              /* comment
            """;

        var exception = Assert.Throws<SyntaxException>(() => new Lexer(source).Tokenize());

        Assert.Equal(2, exception.Line);
        Assert.Equal(3, exception.Column);
        Assert.Equal("/*", exception.TokenText);
        Assert.Contains("Unterminated multi-line comment", exception.Message);
    }

    [Fact]
    public void BlockComment_DoesNotAffectExecution()
    {
        const string source = """
            var value = 1;
            /* : ; if else while function */
            result = value + 1;
            """;

        var context = new RuleScriptEngine().Execute(source);

        Assert.Equal(2d, context.Get<double>("result"));
    }
}
