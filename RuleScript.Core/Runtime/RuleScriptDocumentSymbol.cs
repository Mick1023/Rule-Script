namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies the kind of declaration shown in a document outline.
/// </summary>
public enum RuleScriptDocumentSymbolKind
{
    Function,
    Variable,
    Constant,
    Import
}

/// <summary>
/// Describes a declaration in a document outline.
/// </summary>
/// <param name="Name">The declared name, or the import path when an import has no alias.</param>
/// <param name="Kind">The declaration kind.</param>
/// <param name="Detail">Extra display text such as a function signature or import path.</param>
/// <param name="Range">The full source range of the declaration.</param>
/// <param name="SelectionRange">The range of the declared name.</param>
/// <param name="Children">Declarations nested inside this declaration.</param>
public sealed record RuleScriptDocumentSymbol(
    string Name,
    RuleScriptDocumentSymbolKind Kind,
    string? Detail,
    RuleScriptSourceRange Range,
    RuleScriptSourceRange SelectionRange,
    IReadOnlyList<RuleScriptDocumentSymbol> Children);
