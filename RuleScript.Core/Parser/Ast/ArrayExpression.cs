namespace RuleScript.Core.Parser.Ast;

public sealed record ArrayExpression(IReadOnlyList<Expression> Elements) : Expression;
