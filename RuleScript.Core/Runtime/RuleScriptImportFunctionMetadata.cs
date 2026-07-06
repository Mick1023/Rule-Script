namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes import metadata for a function symbol exposed from another module.
/// </summary>
public sealed record RuleScriptImportFunctionMetadata(
    string SourcePath,
    string? Alias,
    string OriginalName);
