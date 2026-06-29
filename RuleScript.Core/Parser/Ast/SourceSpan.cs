namespace RuleScript.Core.Parser.Ast;

internal sealed record SourceSpan(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
