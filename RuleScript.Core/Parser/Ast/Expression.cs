using RuleScript.Core.Runtime;

namespace RuleScript.Core.Parser.Ast;

public abstract record Expression
{
    internal SourceSpan? SourceSpan { get; set; }

    /// <summary>
    /// Gets the source range covered by this expression when it was produced by the parser.
    /// </summary>
    public RuleScriptSourceRange? Range => RuleScriptSourceMapper.CreateRange(null, SourceSpan);
}
