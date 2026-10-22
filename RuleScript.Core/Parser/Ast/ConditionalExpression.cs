namespace RuleScript.Core.Parser.Ast;

/// <summary>
/// A conditional expression <c>condition ? whenTrue : whenFalse</c>. Only the selected branch is evaluated.
/// </summary>
public sealed record ConditionalExpression(
    Expression Condition,
    Expression WhenTrue,
    Expression WhenFalse,
    int? Line = null,
    int? Column = null) : Expression;
