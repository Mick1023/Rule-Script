namespace RuleScript.Core.Parser.Ast;

public sealed record IndexExpression(
    Expression Target,
    Expression Index,
    int? Line = null,
    int? Column = null) : Expression;
