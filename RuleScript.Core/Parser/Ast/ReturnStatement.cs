namespace RuleScript.Core.Parser.Ast;

public sealed record ReturnStatement(
    Expression? Value,
    int? Line = null,
    int? Column = null) : Statement;
