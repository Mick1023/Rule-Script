namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a breakpoint location.
/// </summary>
/// <param name="File">The source file for the breakpoint, or <see langword="null"/> to match any file.</param>
/// <param name="Line">The 1-based source line for the breakpoint.</param>
public sealed record RuleScriptBreakpoint(string? File, int Line)
{
    public RuleScriptBreakpoint(string? file, int line, string? condition)
        : this(file, line)
    {
        Condition = condition;
    }

    /// <summary>
    /// Gets an optional RuleScript boolean expression that must be true to pause.
    /// </summary>
    public string? Condition { get; init; }
}
