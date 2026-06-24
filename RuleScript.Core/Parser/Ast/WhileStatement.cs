namespace RuleScript.Core.Parser.Ast;

public sealed record WhileStatement(
    Expression Condition,
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null) : Statement;
