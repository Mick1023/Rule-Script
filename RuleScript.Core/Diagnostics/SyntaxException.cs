namespace RuleScript.Core.Diagnostics;

public sealed class SyntaxException : RuleScriptException
{
    public SyntaxException(string message, int? line = null, int? column = null, string? tokenText = null)
        : base(message, line, column, tokenText)
    {
    }

    public SyntaxException(string message, Exception innerException)
        : base(message, innerException: innerException)
    {
    }
}
