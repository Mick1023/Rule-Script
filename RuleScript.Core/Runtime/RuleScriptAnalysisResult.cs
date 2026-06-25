namespace RuleScript.Core.Runtime;

/// <summary>
/// Represents static script symbols collected for editor tooling.
/// </summary>
public sealed class RuleScriptAnalysisResult
{
    internal RuleScriptAnalysisResult(
        IEnumerable<string> variableNames,
        IEnumerable<string> userFunctionNames,
        IEnumerable<string> hostFunctionNames,
        IEnumerable<string> builtinFunctionNames,
        IEnumerable<string> importAliases)
    {
        VariableNames = Snapshot(variableNames);
        UserFunctionNames = Snapshot(userFunctionNames);
        HostFunctionNames = Snapshot(hostFunctionNames);
        BuiltinFunctionNames = Snapshot(builtinFunctionNames);
        FunctionNames = Snapshot(UserFunctionNames.Concat(HostFunctionNames).Concat(BuiltinFunctionNames));
        ImportAliases = Snapshot(importAliases);
    }

    /// <summary>
    /// Gets variable names declared or assigned in the script.
    /// </summary>
    public IReadOnlyList<string> VariableNames { get; }

    /// <summary>
    /// Gets user-defined function names declared in the script.
    /// </summary>
    public IReadOnlyList<string> UserFunctionNames { get; }

    /// <summary>
    /// Gets host function names currently registered on the engine.
    /// </summary>
    public IReadOnlyList<string> HostFunctionNames { get; }

    /// <summary>
    /// Gets built-in function names available to the script.
    /// </summary>
    public IReadOnlyList<string> BuiltinFunctionNames { get; }

    /// <summary>
    /// Gets all callable function names available from the current script and engine.
    /// </summary>
    public IReadOnlyList<string> FunctionNames { get; }

    /// <summary>
    /// Gets import aliases declared in the script.
    /// </summary>
    public IReadOnlyList<string> ImportAliases { get; }

    private static IReadOnlyList<string> Snapshot(IEnumerable<string> names)
    {
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
