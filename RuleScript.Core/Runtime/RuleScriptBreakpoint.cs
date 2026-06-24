namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a breakpoint location.
/// </summary>
/// <param name="File">The source file for the breakpoint, or <see langword="null"/> to match any file.</param>
/// <param name="Line">The 1-based source line for the breakpoint.</param>
public sealed record RuleScriptBreakpoint(string? File, int Line);
