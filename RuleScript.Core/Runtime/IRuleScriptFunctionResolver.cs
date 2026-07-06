namespace RuleScript.Core.Runtime;

/// <summary>
/// Resolves RuleScript function symbols by script-visible name.
/// </summary>
public interface IRuleScriptFunctionResolver
{
    RuleScriptFunctionSymbol? ResolveFunction(string name);

    IReadOnlyList<RuleScriptFunctionSymbol> ResolveFunctions(string name);
}
