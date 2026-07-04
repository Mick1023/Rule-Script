using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal static class RuleScriptSourceMapper
{
    public static RuleScriptSourceLocation? CreateLocation(string? file, int? line, int? column)
    {
        return line.HasValue || column.HasValue
            ? new RuleScriptSourceLocation(file, line, column)
            : null;
    }

    public static RuleScriptSourceRange? CreateRange(string? file, SourceSpan? span)
    {
        return span is null
            ? null
            : new RuleScriptSourceRange(
                file,
                span.StartLine,
                span.StartColumn,
                span.EndLine,
                span.EndColumn);
    }

    public static RuleScriptSourceRange? CreateTokenRange(
        string? file,
        int? line,
        int? column,
        string? tokenText)
    {
        return CreateTokenRange(file, line, column, endLine: null, endColumn: null, tokenText);
    }

    public static RuleScriptSourceRange? CreateTokenRange(
        string? file,
        int? line,
        int? column,
        int? endLine,
        int? endColumn,
        string? tokenText)
    {
        return line.HasValue && column.HasValue
            ? new RuleScriptSourceRange(
                file,
                line.Value,
                column.Value,
                endLine ?? line.Value,
                endColumn ?? column.Value + Math.Max(tokenText?.Length ?? 0, 1))
            : null;
    }

    public static RuleScriptSourceLocation? WithFile(RuleScriptSourceLocation? location, string file)
    {
        return location is null
            ? new RuleScriptSourceLocation(file, null, null)
            : location with { File = file };
    }

    public static RuleScriptSourceRange? WithFile(RuleScriptSourceRange? range, string file)
    {
        return range is null
            ? null
            : range with { File = file };
    }

    public static bool Contains(SourceSpan? span, int line, int column)
    {
        if (span is null)
        {
            return false;
        }

        var afterStart = line > span.StartLine || (line == span.StartLine && column >= span.StartColumn);
        var beforeEnd = line < span.EndLine || (line == span.EndLine && column < span.EndColumn);
        return afterStart && beforeEnd;
    }
}
