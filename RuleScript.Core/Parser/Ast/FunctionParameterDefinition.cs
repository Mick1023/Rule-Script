namespace RuleScript.Core.Parser.Ast;

public sealed record FunctionParameterDefinition(
    string Name,
    string? TypeName = null,
    int? Line = null,
    int? Column = null);
