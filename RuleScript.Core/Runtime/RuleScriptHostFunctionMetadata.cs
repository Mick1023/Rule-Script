namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes host-specific execution metadata for a function symbol.
/// </summary>
public sealed record RuleScriptHostFunctionMetadata(
    bool IsAsync = false,
    bool IsThreadSafe = false,
    bool IsVariadic = false);
