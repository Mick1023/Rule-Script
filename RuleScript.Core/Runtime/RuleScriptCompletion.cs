namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies what a completion item inserts.
/// </summary>
public enum RuleScriptCompletionItemKind
{
    Keyword,
    Snippet,
    Variable,
    Constant,
    Parameter,
    Function,
    HostFunction,
    BuiltinFunction,
    Module,
    Attribute
}

/// <summary>
/// Describes one completion candidate.
/// </summary>
/// <param name="Label">The text shown in the completion list.</param>
/// <param name="Kind">The candidate kind.</param>
/// <param name="Detail">A short description such as a signature or type.</param>
/// <param name="Documentation">Documentation comments associated with the candidate.</param>
/// <param name="InsertText">The text to insert. Uses LSP snippet syntax when <paramref name="IsSnippet"/> is true.</param>
/// <param name="IsSnippet">Whether <paramref name="InsertText"/> contains snippet placeholders such as <c>${1:condition}</c>.</param>
public sealed record RuleScriptCompletionItem(
    string Label,
    RuleScriptCompletionItemKind Kind,
    string? Detail,
    string? Documentation,
    string InsertText,
    bool IsSnippet = false);

/// <summary>
/// Completion candidates for a cursor position. Clients filter candidates by the partially typed word.
/// </summary>
/// <param name="Items">The completion candidates.</param>
/// <param name="ReplaceRange">The range of the partially typed word that a selected candidate replaces.</param>
public sealed record RuleScriptCompletionList(
    IReadOnlyList<RuleScriptCompletionItem> Items,
    RuleScriptSourceRange ReplaceRange);
