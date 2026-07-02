namespace RuleScript.Core.Parser.Ast;

public sealed record ParallelStatementSyntax(
    IReadOnlyList<TaskBlockSyntax> Tasks,
    int? Line = null,
    int? Column = null) : Statement;
