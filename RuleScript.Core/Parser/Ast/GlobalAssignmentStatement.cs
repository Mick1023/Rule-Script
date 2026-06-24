namespace RuleScript.Core.Parser.Ast;

public sealed record GlobalAssignmentStatement(
    string Name,
    Expression Value,
    int? Line = null,
    int? Column = null) : Statement;
