namespace RuleScript.Core.Lexer;

public sealed record Token(
    TokenType Type,
    string Lexeme,
    object? Literal,
    int Line,
    int Column)
{
    public int EndLine => Line;

    public int EndColumn => Column + Lexeme.Length;
}
