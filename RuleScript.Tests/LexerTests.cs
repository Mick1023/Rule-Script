using RuleScript.Core.Lexer;

namespace RuleScript.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Tokenize_SkipsLineCommentsAndReadsPhaseOneTokens()
    {
        var lexer = new Lexer("""
            // comment
            var result = "OK";
            if result != "NG" then:
            endif
            """);

        var tokens = lexer.Tokenize().Select(token => token.Type).ToArray();

        Assert.Equal(
            [
                TokenType.Var,
                TokenType.Identifier,
                TokenType.Assign,
                TokenType.String,
                TokenType.Semicolon,
                TokenType.If,
                TokenType.Identifier,
                TokenType.BangEqual,
                TokenType.String,
                TokenType.Then,
                TokenType.Colon,
                TokenType.EndIf,
                TokenType.EndOfFile
            ],
            tokens);
    }
}
