namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a variable discovered by static analysis.
/// </summary>
public sealed record RuleScriptVariableSymbol(
    string Name,
    RuleScriptValueType Type,
    bool IsReadOnly = false,
    bool IsExported = false);
