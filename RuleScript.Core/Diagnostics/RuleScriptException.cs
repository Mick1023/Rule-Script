namespace RuleScript.Core.Diagnostics;

public abstract class RuleScriptException : Exception
{
    protected RuleScriptException(
        string message,
        int? line = null,
        int? column = null,
        string? tokenText = null,
        Exception? innerException = null)
        : base(FormatMessage(message, line, column), innerException)
    {
        Line = line;
        Column = column;
        TokenText = tokenText;
    }

    public int? Line { get; }

    public int? Column { get; }

    public string? TokenText { get; }

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
