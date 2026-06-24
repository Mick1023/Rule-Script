namespace RuleScript.Core.Parser.Ast;

public sealed record ForeachStatement(
    string VariableName,
    Expression Iterable,
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null) : Statement;
