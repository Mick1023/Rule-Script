namespace RuleScript.Core.Diagnostics;

/// <summary>
/// Represents a RuleScript lexing or parsing failure.
/// </summary>
public sealed class SyntaxException : RuleScriptException
{
    /// <summary>
    /// Creates a syntax exception.
    /// </summary>
    public SyntaxException(
        string message,
        int? line = null,
        int? column = null,
        string? tokenText = null,
        string? sourceFile = null)
        : base(FormatSyntaxMessage(message), line, column, tokenText, sourceFile)
    {
    }

    /// <summary>
    /// Creates a syntax exception with a full source range.
    /// </summary>
    public SyntaxException(
        string message,
        int? line,
        int? column,
        string? tokenText,
        string? sourceFile,
        int? endLine,
        int? endColumn)
        : base(FormatSyntaxMessage(message), line, column, tokenText, sourceFile, endLine, endColumn, null)
    {
    }

    /// <summary>
    /// Creates a syntax exception with an inner exception.
    /// </summary>
    public SyntaxException(string message, Exception innerException)
        : base(FormatSyntaxMessage(message), innerException: innerException)
    {
    }

    private static string FormatSyntaxMessage(string message)
    {
        return message.StartsWith("Syntax error:", StringComparison.Ordinal)
            ? message
            : $"Syntax error: {message}";
    }
}
