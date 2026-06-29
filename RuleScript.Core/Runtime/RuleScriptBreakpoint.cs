namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a breakpoint location.
/// </summary>
/// <param name="File">The source file for the breakpoint, or <see langword="null"/> to match any file.</param>
/// <param name="Line">The 1-based source line for the breakpoint.</param>
/// <param name="Condition">An optional RuleScript boolean expression that must be true to pause.</param>
public sealed record RuleScriptBreakpoint(string? File, int Line, string? Condition = null);
