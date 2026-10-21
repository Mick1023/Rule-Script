namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes one parameter inside a signature label.
/// </summary>
/// <param name="Label">The parameter text, such as <c>value: number</c>.</param>
/// <param name="Start">The zero-based offset of the parameter inside the signature label.</param>
/// <param name="End">The exclusive zero-based end offset of the parameter inside the signature label.</param>
public sealed record RuleScriptParameterInfo(string Label, int Start, int End);

/// <summary>
/// Describes one callable signature.
/// </summary>
/// <param name="Label">The signature text, such as <c>AddOne(value: number) -> number</c>.</param>
/// <param name="Documentation">Documentation comments associated with the function.</param>
/// <param name="Parameters">The parameters, in declaration order.</param>
public sealed record RuleScriptSignatureInfo(
    string Label,
    string? Documentation,
    IReadOnlyList<RuleScriptParameterInfo> Parameters);

/// <summary>
/// Signature help for the call surrounding a cursor position.
/// </summary>
/// <param name="Signatures">Candidate signatures for the called function.</param>
/// <param name="ActiveSignature">The index of the signature that best matches the typed arguments.</param>
/// <param name="ActiveParameter">The zero-based index of the argument under the cursor.</param>
public sealed record RuleScriptSignatureHelp(
    IReadOnlyList<RuleScriptSignatureInfo> Signatures,
    int ActiveSignature,
    int ActiveParameter);
