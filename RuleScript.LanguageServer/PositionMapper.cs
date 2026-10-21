using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RuleScript.Core.Runtime;

namespace RuleScript.LanguageServer;

/// <summary>
/// Converts between RuleScript 1-based line/column positions and LSP 0-based positions.
/// Both count columns in UTF-16 code units, so only the base differs.
/// </summary>
internal static class PositionMapper
{
    public static (int Line, int Column) ToRuleScript(Position position)
    {
        return (position.Line + 1, position.Character + 1);
    }

    public static Position ToLsp(int line, int column)
    {
        return new Position(Math.Max(0, line - 1), Math.Max(0, column - 1));
    }

    public static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range ToLsp(RuleScriptSourceRange range)
    {
        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            ToLsp(range.StartLine, range.StartColumn),
            ToLsp(range.EndLine, range.EndColumn));
    }
}
