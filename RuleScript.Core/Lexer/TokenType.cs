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
    While,
    EndWhile,
    Foreach,
    In,
    EndForeach,
    Function,
    EndFunction,
    Return,
    Break,
    Continue,

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
    LeftBracket,
    RightBracket,
    Dot,
    Comma,
    Semicolon,
    Colon
}
