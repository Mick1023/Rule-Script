namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes whether the symbol at a position can be renamed.
/// </summary>
/// <param name="CanRename">Whether a rename can start at the position.</param>
/// <param name="RejectReason">Why the symbol cannot be renamed, when <paramref name="CanRename"/> is false.</param>
/// <param name="Name">The current symbol name.</param>
/// <param name="Range">The range of the symbol occurrence at the position.</param>
public sealed record RuleScriptPrepareRenameResult(
    bool CanRename,
    string? RejectReason,
    string? Name,
    RuleScriptSourceRange? Range);

/// <summary>
/// The text edits that rename a symbol, or the reason the rename was rejected.
/// </summary>
/// <param name="Success">Whether the rename can be applied.</param>
/// <param name="ErrorMessage">Why the rename was rejected, when <paramref name="Success"/> is false.</param>
/// <param name="Edits">Ranges to replace with <paramref name="NewName"/>. A null <see cref="RuleScriptSourceRange.File"/> means the analyzed document.</param>
/// <param name="NewName">The replacement name.</param>
public sealed record RuleScriptRenameResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<RuleScriptSourceRange> Edits,
    string NewName);
