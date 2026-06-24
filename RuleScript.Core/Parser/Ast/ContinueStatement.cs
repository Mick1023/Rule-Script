namespace RuleScript.Core.Parser.Ast;

public sealed record ContinueStatement(int? Line = null, int? Column = null) : Statement;
