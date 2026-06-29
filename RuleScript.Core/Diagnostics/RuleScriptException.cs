namespace RuleScript.Core.Diagnostics;

/// <summary>
/// Base exception type for RuleScript syntax and runtime failures.
/// </summary>
public abstract class RuleScriptException : Exception
{
    private readonly string _message;

    protected RuleScriptException(
        string message,
        int? line = null,
        int? column = null,
        string? tokenText = null,
        string? sourceFile = null,
        int? endLine = null,
        int? endColumn = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        _message = message;
        Line = line;
        Column = column;
        TokenText = tokenText;
        SourceFile = sourceFile;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    /// <inheritdoc />
    public override string Message => FormatMessage(_message, SourceFile, Line, Column);

    /// <summary>
    /// Gets the 1-based source line when available.
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// Gets the 1-based source column when available.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Gets the 1-based ending source line when available.
    /// </summary>
    public int? EndLine { get; }

    /// <summary>
    /// Gets the exclusive 1-based ending source column when available.
    /// </summary>
    public int? EndColumn { get; }

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

    private static string FormatMessage(string message, string? sourceFile, int? line, int? column)
    {
        var location = sourceFile is not null && line.HasValue && column.HasValue
            ? $"File '{sourceFile}', Line {line.Value}, Column {column.Value}"
            : sourceFile is not null
                ? $"File '{sourceFile}'"
                : line.HasValue && column.HasValue
                    ? $"Line {line.Value}, Column {column.Value}"
                    : null;

        return location is null ? message : $"{location}: {message}";
    }
}
