namespace RuleScript.Core.Parser.Ast;

public sealed record TaskBlockSyntax(
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null);
