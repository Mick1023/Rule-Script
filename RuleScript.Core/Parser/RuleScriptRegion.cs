namespace RuleScript.Core.Parser;

/// <summary>
/// Describes a named source region for editor folding and navigation.
/// </summary>
public sealed record RuleScriptRegion(
    string Name,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
