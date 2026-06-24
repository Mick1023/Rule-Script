namespace RuleScript.Core.Parser.Ast;

public sealed record BreakStatement(int? Line = null, int? Column = null) : Statement;
