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

    public bool IsDeclaration { get; }

    public bool IsExternal { get; }
}
