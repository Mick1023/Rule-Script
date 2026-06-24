namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies a location in RuleScript source code.
/// </summary>
/// <param name="File">The source file when available.</param>
/// <param name="Line">The 1-based source line when available.</param>
/// <param name="Column">The 1-based source column when available.</param>
public sealed record RuleScriptSourceLocation(string? File, int? Line, int? Column);
