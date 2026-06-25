namespace RuleScript.Core.Runtime;

/// <summary>
/// Represents the result of a best-effort script analysis.
/// </summary>
public sealed class RuleScriptAnalysisAttempt
{
    internal RuleScriptAnalysisAttempt(
        RuleScriptAnalysisResult symbols,
        IEnumerable<RuleScriptDiagnostic> diagnostics,
        bool success)
    {
        Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        Diagnostics = diagnostics.ToArray();
        Success = success;
    }

    /// <summary>
    /// Gets whether the script was fully parsed without diagnostics.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the best-effort symbols collected from the script.
    /// </summary>
    public RuleScriptAnalysisResult Symbols { get; }

    /// <summary>
    /// Gets diagnostics produced while analyzing the script.
    /// </summary>
    public IReadOnlyList<RuleScriptDiagnostic> Diagnostics { get; }
}
