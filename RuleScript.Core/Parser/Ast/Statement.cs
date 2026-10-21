using RuleScript.Core.Runtime;

namespace RuleScript.Core.Parser.Ast;

public abstract record Statement
{
    internal SourceSpan? SourceSpan { get; set; }

    /// <summary>
    /// Gets the source range covered by this statement when it was produced by the parser.
    /// </summary>
    public RuleScriptSourceRange? Range => RuleScriptSourceMapper.CreateRange(null, SourceSpan);
}
