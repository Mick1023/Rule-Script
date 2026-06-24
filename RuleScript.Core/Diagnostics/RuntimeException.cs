namespace RuleScript.Core.Diagnostics;

public sealed class RuntimeException : RuleScriptException
{
    public RuntimeException(string message, int? line = null, int? column = null, string? tokenText = null)
        : base(FormatRuntimeMessage(message), line, column, tokenText)
    {
    }

    public RuntimeException(string message, Exception innerException)
        : base(FormatRuntimeMessage(message), innerException: innerException)
    {
    }

    public RuntimeException(string message, Exception innerException, int? line, int? column, string? tokenText = null)
        : base(FormatRuntimeMessage(message), line, column, tokenText, innerException)
    {
    }

    private static string FormatRuntimeMessage(string message)
    {
        return message.StartsWith("Runtime error:", StringComparison.Ordinal)
            ? message
            : $"Runtime error: {message}";
    }
}
