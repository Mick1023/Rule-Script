namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a built-in function signature available to RuleScript analysis tools.
/// </summary>
public sealed class RuleScriptBuiltinFunctionSymbol
{
    public RuleScriptBuiltinFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }
}
