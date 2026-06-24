namespace RuleScript.Core.Parser.Ast;

public sealed record FunctionCallExpression(
    string Name,
    IReadOnlyList<Expression> Arguments,
    int? Line = null,
    int? Column = null) : Expression;
