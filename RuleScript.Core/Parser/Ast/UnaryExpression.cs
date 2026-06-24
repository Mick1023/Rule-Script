using RuleScript.Core.Lexer;

namespace RuleScript.Core.Parser.Ast;

public sealed record UnaryExpression(
    TokenType Operator,
    Expression Operand,
    int? Line = null,
    int? Column = null,
    string? TokenText = null) : Expression;
