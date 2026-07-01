namespace RuleScript.Core.Parser.Ast;

public sealed record TargetAssignmentStatement(
    Expression Target,
    Expression Value,
    int? Line = null,
    int? Column = null) : Statement;
