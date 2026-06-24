namespace RuleScript.Core.Parser.Ast;

public sealed record ImportStatement(
    string Path,
    string? Alias,
    int? Line = null,
    int? Column = null) : Statement;
