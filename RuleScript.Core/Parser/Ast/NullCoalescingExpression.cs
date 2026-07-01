namespace RuleScript.Core.Parser.Ast;

public sealed record NullCoalescingExpression(
    Expression Left,
    Expression Right,
    int? Line = null,
    int? Column = null) : Expression;
