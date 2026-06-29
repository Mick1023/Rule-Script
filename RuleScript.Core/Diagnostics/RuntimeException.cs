namespace RuleScript.Core.Diagnostics;

/// <summary>
/// Represents a RuleScript runtime failure.
/// </summary>
public sealed class RuntimeException : RuleScriptException
{
    /// <summary>
    /// Creates a runtime exception.
    /// </summary>
    public RuntimeException(string message, int? line = null, int? column = null, string? tokenText = null, string? sourceFile = null)
        : base(FormatRuntimeMessage(message), line, column, tokenText, sourceFile)
    {
    }

    /// <summary>
    /// Creates a runtime exception with an inner exception.
    /// </summary>
    public RuntimeException(string message, Exception innerException)
        : base(FormatRuntimeMessage(message), innerException: innerException)
    {
    }

    /// <summary>
    /// Creates a runtime exception with source location and an inner exception.
    /// </summary>
    public RuntimeException(string message, Exception innerException, int? line, int? column, string? tokenText = null, string? sourceFile = null)
        : base(FormatRuntimeMessage(message), line, column, tokenText, sourceFile, innerException: innerException)
    {
    }

    private static string FormatRuntimeMessage(string message)
    {
        return message.StartsWith("Runtime error:", StringComparison.Ordinal)
            ? message
            : $"Runtime error: {message}";
    }
}
