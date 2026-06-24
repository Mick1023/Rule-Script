namespace RuleScript.Core.Lexer;

public sealed record Token(
    TokenType Type,
    string Lexeme,
    object? Literal,
    int Line,
    int Column);
