using RuleScript.Core.Diagnostics;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Parser;

/// <summary>
/// Represents a best-effort parse with every recoverable syntax diagnostic.
/// </summary>
public sealed class RuleScriptParseResult
{
    internal RuleScriptParseResult(
        IEnumerable<Statement> statements,
        IEnumerable<SyntaxException> diagnostics)
    {
        Statements = statements.ToArray();
        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<Statement> Statements { get; }

    public IReadOnlyList<SyntaxException> Diagnostics { get; }

    public bool Success => Diagnostics.Count == 0;
}
