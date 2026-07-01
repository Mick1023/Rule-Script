namespace RuleScript.Core.Parser.Ast;

public sealed record ObjectLiteralExpression(
    IReadOnlyList<ObjectProperty> Properties,
    int? Line = null,
    int? Column = null) : Expression;
