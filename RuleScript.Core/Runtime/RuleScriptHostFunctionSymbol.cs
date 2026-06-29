namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a registered host function signature.
/// </summary>
public sealed class RuleScriptHostFunctionSymbol
{
    public RuleScriptHostFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        bool isAsync = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
        IsAsync = isAsync;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }

    public bool IsAsync { get; }
}
