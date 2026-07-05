namespace RuleScript.Core.Runtime;

internal static class RuleScriptFunctionSignatureComparer
{
    public static string GetSignatureKey(RuleScriptFunctionSymbol function)
    {
        return RuleScriptFunctionSymbol.CreateSignatureKey(function.Name, function.Parameters);
    }

    public static string GetSignatureDisplay(RuleScriptFunctionSymbol function)
    {
        return function.Signature;
    }
}
