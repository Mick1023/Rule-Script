namespace RuleScript.Core.Parser.Ast;

public enum TaskBlockKind
{
    Normal,
    Trigger
}

public sealed record TaskBlockSyntax(
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null)
{
    public TaskBlockKind Kind { get; init; } = TaskBlockKind.Normal;
}
