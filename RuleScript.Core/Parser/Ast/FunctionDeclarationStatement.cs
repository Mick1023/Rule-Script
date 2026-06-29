namespace RuleScript.Core.Parser.Ast;

public sealed record FunctionDeclarationStatement(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null) : Statement
{
    public IReadOnlyList<FunctionParameterDefinition> ParameterDefinitions { get; init; } =
        Parameters.Select(parameter => new FunctionParameterDefinition(parameter)).ToArray();
}
