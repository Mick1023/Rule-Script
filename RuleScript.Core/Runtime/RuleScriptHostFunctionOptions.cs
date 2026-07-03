namespace RuleScript.Core.Runtime;

/// <summary>
/// Configures host function execution and editor metadata without requiring additional registration overloads.
/// </summary>
public sealed class RuleScriptHostFunctionOptions
{
    /// <summary>
    /// Gets the fixed parameter signature. Null keeps the legacy untyped, variadic behavior.
    /// </summary>
    public IReadOnlyList<RuleScriptParameterSymbol>? Parameters { get; init; }

    /// <summary>
    /// Gets the declared return type. Untyped registrations default to <see cref="RuleScriptValueType.Unknown"/>.
    /// </summary>
    public RuleScriptValueType ReturnType { get; init; } = RuleScriptValueType.Unknown;

    /// <summary>
    /// Gets whether the host function may be called concurrently from parallel tasks.
    /// </summary>
    public bool ThreadSafe { get; init; }

    /// <summary>
    /// Gets documentation exposed through host function analysis metadata.
    /// </summary>
    public string? Documentation { get; init; }
}
