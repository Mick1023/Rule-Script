namespace RuleScript.Core.Parser.Ast;

public abstract record Statement
{
    internal SourceSpan? SourceSpan { get; set; }
}
