namespace RuleScript.Core.Parser.Ast;

public sealed record FunctionDeclarationStatement(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null) : Statement;
