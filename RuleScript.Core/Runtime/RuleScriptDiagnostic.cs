namespace RuleScript.Core.Runtime;

/// <summary>
/// Represents a non-throwing diagnostic collected by editor tooling APIs.
/// </summary>
public sealed record RuleScriptDiagnostic(
    string Message,
    int? Line = null,
    int? Column = null,
    string? TokenText = null,
    string? SourceFile = null)
{
    public RuleScriptDiagnostic(
        string message,
        int? line,
        int? column,
        string? tokenText,
        string? sourceFile,
        RuleScriptSourceRange? range)
        : this(message, line, column, tokenText, sourceFile)
    {
        Range = range;
    }

    /// <summary>
    /// Gets the full source range associated with the diagnostic when available.
    /// </summary>
    public RuleScriptSourceRange? Range { get; init; }

    /// <summary>
    /// Gets the stable machine-readable diagnostic code.
    /// </summary>
    public string Code { get; init; } = RuleScriptDiagnosticCodes.SyntaxError;

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public RuleScriptDiagnosticSeverity Severity { get; init; } = RuleScriptDiagnosticSeverity.Error;
}
