namespace RuleScript.Core.Parser.Ast;

public sealed record GlobalIdentifierExpression(
    string Name,
    int? Line = null,
    int? Column = null) : Expression;
