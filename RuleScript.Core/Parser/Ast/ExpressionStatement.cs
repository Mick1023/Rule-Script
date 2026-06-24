namespace RuleScript.Core.Parser.Ast;

public sealed record ExpressionStatement(Expression Expression) : Statement;
