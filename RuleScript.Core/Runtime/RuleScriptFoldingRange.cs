namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies why a source range can be folded.
/// </summary>
public enum RuleScriptFoldingRangeKind
{
    Region,
    Block,
    Comment
}

/// <summary>
/// Describes a foldable line range. Lines are 1-based and inclusive; for blocks, <paramref name="EndLine"/> is the line of the closing keyword.
/// </summary>
public sealed record RuleScriptFoldingRange(
    int StartLine,
    int EndLine,
    RuleScriptFoldingRangeKind Kind);
