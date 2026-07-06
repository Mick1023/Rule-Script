using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Captures the parsed and analyzed state for a single RuleScript document so editor features can reuse it.
/// </summary>
public sealed class RuleScriptDocumentAnalysisResult
{
    internal RuleScriptDocumentAnalysisResult(
        RuleScriptEngine engine,
        string source,
        IEnumerable<Token> tokens,
        IEnumerable<Statement> statements,
        IEnumerable<RuleScriptRegion> regions,
        RuleScriptAnalysisResult analysis)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Tokens = tokens?.ToArray() ?? throw new ArgumentNullException(nameof(tokens));
        Statements = statements?.ToArray() ?? throw new ArgumentNullException(nameof(statements));
        Regions = regions?.ToArray() ?? throw new ArgumentNullException(nameof(regions));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
    }

    internal RuleScriptEngine Engine { get; }

    /// <summary>
    /// Gets the source text that produced this analysis result.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the lexer tokens parsed for this document.
    /// </summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>
    /// Gets the parsed top-level statements for this document.
    /// </summary>
    public IReadOnlyList<Statement> Statements { get; }

    /// <summary>
    /// Gets parsed named region metadata for folding providers.
    /// </summary>
    public IReadOnlyList<RuleScriptRegion> Regions { get; }

    /// <summary>
    /// Gets semantic analysis, diagnostics, and editor-facing symbols for this document.
    /// </summary>
    public RuleScriptAnalysisResult Analysis { get; }
}
