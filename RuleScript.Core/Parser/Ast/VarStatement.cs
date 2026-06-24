namespace RuleScript.Core.Parser.Ast;

public sealed record VarStatement(string Name, Expression? Initializer, int? Line = null, int? Column = null) : Statement;
