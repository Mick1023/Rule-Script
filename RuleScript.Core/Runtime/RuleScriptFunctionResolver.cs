namespace RuleScript.Core.Runtime;

/// <summary>
/// Provides a snapshot-based lookup for RuleScript function symbols.
/// </summary>
public sealed class RuleScriptFunctionResolver : IRuleScriptFunctionResolver
{
    private readonly IReadOnlyDictionary<string, RuleScriptFunctionSymbol> _functions;

    public RuleScriptFunctionResolver(IEnumerable<RuleScriptFunctionSymbol> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);

        _functions = functions
            .Where(function => !string.IsNullOrWhiteSpace(function.Name))
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        Functions = _functions.Values
            .OrderBy(function => function.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<RuleScriptFunctionSymbol> Functions { get; }

    public RuleScriptFunctionSymbol? ResolveFunction(string name)
    {
        return _functions.TryGetValue(name, out var function) ? function : null;
    }
}
