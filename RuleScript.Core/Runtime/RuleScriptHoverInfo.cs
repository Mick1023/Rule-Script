namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes the symbol under a source position for editor hover providers.
/// </summary>
/// <param name="Name">The symbol name.</param>
/// <param name="Kind">The symbol kind.</param>
/// <param name="Signature">A RuleScript-style declaration line, such as <c>function AddOne(value: number) -> number</c>.</param>
/// <param name="Documentation">Documentation comments associated with the symbol when available.</param>
/// <param name="Range">The source range of the hovered symbol occurrence.</param>
/// <param name="IsExternal">Whether the symbol is provided by the host or built-in library.</param>
public sealed record RuleScriptHoverInfo(
    string Name,
    RuleScriptSymbolKind Kind,
    string Signature,
    string? Documentation,
    RuleScriptSourceRange? Range,
    bool IsExternal);
