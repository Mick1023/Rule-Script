namespace RuleScript.Core.Parser.Ast;

public sealed record SwitchStatement(
    Expression Value,
    IReadOnlyList<SwitchCase> Cases,
    IReadOnlyList<Statement>? DefaultBranch,
    int? Line = null,
    int? Column = null) : Statement;
