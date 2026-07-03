namespace RuleScript.Core.Formatting;

/// <summary>
/// Formats Rule Script source code without changing its execution semantics.
/// </summary>
public static class RuleScriptFormatter
{
    /// <summary>
    /// Formats valid Rule Script source using the canonical indentation and spacing rules.
    /// </summary>
    public static string Format(string source) => RuleScriptFormatterCore.Format(source);
}
