namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies a half-open range in RuleScript source code.
/// </summary>
/// <param name="File">The source file when available.</param>
/// <param name="StartLine">The 1-based start line.</param>
/// <param name="StartColumn">The 1-based start column.</param>
/// <param name="EndLine">The 1-based end line.</param>
/// <param name="EndColumn">The exclusive 1-based end column.</param>
public sealed record RuleScriptSourceRange(
    string? File,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
