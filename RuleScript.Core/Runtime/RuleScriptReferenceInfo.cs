namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a declaration or usage range for a RuleScript symbol.
/// </summary>
public sealed class RuleScriptReferenceInfo
{
    public RuleScriptReferenceInfo(
        string name,
        RuleScriptSymbolKind kind,
        RuleScriptSourceRange? range,
        bool isDeclaration,
        bool isExternal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Kind = kind;
        Range = range;
        IsDeclaration = isDeclaration;
        IsExternal = isExternal;
    }

    public string Name { get; }

    public RuleScriptSymbolKind Kind { get; }

    public RuleScriptSourceRange? Range { get; }

    public string? File => Range?.File;

    public int? Line => Range?.StartLine;

    public int? Column => Range?.StartColumn;

    public int? EndLine => Range?.EndLine;

    public int? EndColumn => Range?.EndColumn;

    public bool IsDeclaration { get; }

    public bool IsExternal { get; }
}
