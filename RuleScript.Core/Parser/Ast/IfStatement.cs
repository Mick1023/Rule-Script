namespace RuleScript.Core.Parser.Ast;

public sealed record IfStatement(
    Expression Condition,
    IReadOnlyList<Statement> ThenBranch,
    IReadOnlyList<Statement> ElseBranch,
    int? Line = null,
    int? Column = null) : Statement;
