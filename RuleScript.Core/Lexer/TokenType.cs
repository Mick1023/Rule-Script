namespace RuleScript.Core.Lexer;

public enum TokenType
{
    EndOfFile,

    Identifier,
    Number,
    String,
    True,
    False,

    Var,
    If,
    Then,
    Else,
    EndIf,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    EqualEqual,
    BangEqual,

    Assign,
    Bang,

    LeftParen,
    RightParen,
    Comma,
    Semicolon,
    Colon
}
