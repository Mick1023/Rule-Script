namespace RuleScript.Core.Runtime;

/// <summary>
/// Represents a non-throwing diagnostic collected by editor tooling APIs.
/// </summary>
public sealed record RuleScriptDiagnostic(
    string Message,
    int? Line = null,
    int? Column = null,
    string? TokenText = null,
    string? SourceFile = null);
