namespace RuleScript.Core.Parser.Ast;

public sealed record SwitchLabel(
    Expression Value,
    Expression? Guard = null,
    int? Line = null,
    int? Column = null,
    string? TokenText = null);
