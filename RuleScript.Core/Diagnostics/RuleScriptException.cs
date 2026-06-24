namespace RuleScript.Core.Diagnostics;

/// <summary>
/// Base exception type for RuleScript syntax and runtime failures.
/// </summary>
public abstract class RuleScriptException : Exception
{
    protected RuleScriptException(
        string message,
        int? line = null,
        int? column = null,
        string? tokenText = null,
        string? sourceFile = null,
        Exception? innerException = null)
        : base(FormatMessage(message, line, column), innerException)
    {
        Line = line;
        Column = column;
        TokenText = tokenText;
        SourceFile = sourceFile;
    }

    /// <summary>
    /// Gets the 1-based source line when available.
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// Gets the 1-based source column when available.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Gets the token text or source text associated with the failure when available.
    /// </summary>
    public string? TokenText { get; }

    /// <summary>
    /// Gets the source file associated with the failure when available.
    /// </summary>
    public string? SourceFile { get; internal set; }

    public override string ToString()
    {
        return Message;
    }

    private static string FormatMessage(string message, int? line, int? column)
    {
        return line.HasValue && column.HasValue
            ? $"Line {line.Value}, Column {column.Value}: {message}"
            : message;
    }
}
