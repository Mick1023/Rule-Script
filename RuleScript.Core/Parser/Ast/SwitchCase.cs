namespace RuleScript.Core.Parser.Ast;

public sealed record SwitchCase(
    IReadOnlyList<SwitchLabel> Labels,
    IReadOnlyList<Statement> Body);
