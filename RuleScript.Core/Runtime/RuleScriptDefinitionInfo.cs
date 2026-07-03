namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes the definition target for a RuleScript symbol at a source position.
/// </summary>
public sealed class RuleScriptDefinitionInfo
{
    public RuleScriptDefinitionInfo(
        string name,
        RuleScriptSymbolKind kind,
        RuleScriptSourceRange? range,
        RuleScriptSourceRange? selectionRange,
        string? documentation = null,
        bool isExternal = false,
        IReadOnlyList<RuleScriptParameterSymbol>? parameters = null,
        RuleScriptValueType returnType = RuleScriptValueType.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Kind = kind;
        Range = range;
        SelectionRange = selectionRange;
        Documentation = documentation;
        IsExternal = isExternal;
        Parameters = parameters?.ToArray() ?? [];
        ReturnType = returnType;
    }

    public string Name { get; }

    public RuleScriptSymbolKind Kind { get; }

    public RuleScriptSourceRange? Range { get; }

    public RuleScriptSourceRange? SelectionRange { get; }

    public string? File => SelectionRange?.File ?? Range?.File;

    public int? Line => SelectionRange?.StartLine ?? Range?.StartLine;

    public int? Column => SelectionRange?.StartColumn ?? Range?.StartColumn;

    public int? EndLine => SelectionRange?.EndLine ?? Range?.EndLine;

    public int? EndColumn => SelectionRange?.EndColumn ?? Range?.EndColumn;

    public string? Documentation { get; }

    public bool IsExternal { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }
}
