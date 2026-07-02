namespace RuleScript.Core.Parser.Ast;

public sealed record ParallelExpressionSyntax(
    IReadOnlyList<TaskBlockSyntax> Tasks,
    int? Line = null,
    int? Column = null) : Expression;
