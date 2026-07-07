namespace RuleScript.Core.Parser.Ast;

public sealed record FunctionDeclarationStatement(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Statement> Body,
    string? ReturnTypeName = null,
    int? Line = null,
    int? Column = null) : Statement
{
    public bool IsExported { get; init; }

    public string? Documentation { get; init; }

    public int? NameLine { get; init; }

    public int? NameColumn { get; init; }

    public string? HostTriggerName { get; init; }

    public int? HostTriggerLine { get; init; }

    public int? HostTriggerColumn { get; init; }

    public IReadOnlyList<FunctionParameterDefinition> ParameterDefinitions { get; init; } =
        Parameters.Select(parameter => new FunctionParameterDefinition(parameter)).ToArray();
}
