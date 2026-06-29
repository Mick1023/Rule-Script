using RuleScript.Core.Lexer;

namespace RuleScript.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Tokenize_End_ReturnsGenericBlockEndToken()
    {
        var tokens = new Lexer("end").Tokenize();

        Assert.Equal(TokenType.End, tokens[0].Type);
        Assert.Equal(TokenType.EndOfFile, tokens[1].Type);
    }

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

    [Fact]
    public void Tokenize_AndOr_ReturnsBooleanOperatorTokens()
    {
        var lexer = new Lexer("if a and b or c then: endif");

        var tokens = lexer.Tokenize().Select(token => token.Type).ToArray();

        Assert.Equal(
            [
                TokenType.If,
                TokenType.Identifier,
                TokenType.And,
                TokenType.Identifier,
                TokenType.Or,
                TokenType.Identifier,
                TokenType.Then,
                TokenType.Colon,
                TokenType.EndIf,
                TokenType.EndOfFile
            ],
            tokens);
    }
}
