namespace RuleScript.Core.Parser.Ast;

public sealed record ObjectProperty(
    string Name,
    Expression Value,
    int? Line = null,
    int? Column = null);
