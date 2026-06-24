namespace RuleScript.Core.Parser.Ast;

public sealed record IdentifierExpression(string Name, int? Line = null, int? Column = null) : Expression;
