namespace RuleScript.Core.Parser.Ast;

public sealed record AssignmentStatement(string Name, Expression Value, int? Line = null, int? Column = null) : Statement;
