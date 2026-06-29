namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a user-function parameter discovered by static analysis.
/// </summary>
public sealed record RuleScriptParameterSymbol(string Name, RuleScriptValueType Type);
