namespace RuleScript.Core.Runtime;

/// <summary>
/// Provides a snapshot-based lookup for RuleScript function symbols.
/// </summary>
public sealed class RuleScriptFunctionResolver : IRuleScriptFunctionResolver
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RuleScriptFunctionSymbol>> _functions;

    public RuleScriptFunctionResolver(IEnumerable<RuleScriptFunctionSymbol> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);

        _functions = functions
            .Where(function => !string.IsNullOrWhiteSpace(function.Name))
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RuleScriptFunctionSymbol>)group
                    .GroupBy(RuleScriptFunctionSignatureComparer.GetSignatureKey, StringComparer.Ordinal)
                    .Select(signatureGroup => signatureGroup.Last())
                    .OrderBy(function => function.Signature, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        Functions = _functions.Values
            .SelectMany(function => function)
            .OrderBy(function => function.Name, StringComparer.Ordinal)
            .ThenBy(function => function.Signature, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<RuleScriptFunctionSymbol> Functions { get; }

    public RuleScriptFunctionSymbol? ResolveFunction(string name)
    {
        return _functions.TryGetValue(name, out var functions) ? functions.LastOrDefault() : null;
    }

    public IReadOnlyList<RuleScriptFunctionSymbol> ResolveFunctions(string name)
    {
        return _functions.TryGetValue(name, out var functions) ? functions : [];
    }
}
