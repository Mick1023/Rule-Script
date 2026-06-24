using RuleScript.Core.Lexer;

namespace RuleScript.Core.Parser.Ast;

public sealed record BinaryExpression(
    Expression Left,
    TokenType Operator,
    Expression Right,
    int? Line = null,
    int? Column = null,
    string? TokenText = null) : Expression;
