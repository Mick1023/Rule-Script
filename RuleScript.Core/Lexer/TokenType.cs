namespace RuleScript.Core.Lexer;

public enum TokenType
{
    EndOfFile,

    Identifier,
    Number,
    String,
    True,
    False,
    Null,
    LineComment,
    MultiLineComment,
    DocumentationComment,
    RegionStart,
    RegionEnd,
    At,

    Var,
    Const,
    Export,
    If,
    Then,
    Else,
    ElseIf,
    End,
    EndIf,
    While,
    EndWhile,
    Foreach,
    In,
    EndForeach,
    Function,
    EndFunction,
    Return,
    Import,
    As,
    Break,
    Continue,
    Switch,
    Case,
    Default,
    When,
    EndSwitch,
    Parallel,
    Task,
    Trigger,
    EndTask,
    EndParallel,

    Plus,
    Minus,
    Arrow,
    Star,
    Slash,
    Percent,

    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    EqualEqual,
    BangEqual,
    And,
    Or,

    Assign,
    Bang,
    QuestionQuestion,
    QuestionDot,

    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,
    Dot,
    Comma,
    Semicolon,
    Colon,

    // Appended to keep the numeric values of earlier members stable.
    Question,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    PercentAssign,

    /// <summary>The start of a template string up to its first substitution, such as <c>`Lot ${</c>.</summary>
    TemplateHead,

    /// <summary>Template text between two substitutions, such as <c>} at ${</c>.</summary>
    TemplateMiddle,

    /// <summary>Template text after the last substitution, such as <c>} arrived`</c>.</summary>
    TemplateTail
}
