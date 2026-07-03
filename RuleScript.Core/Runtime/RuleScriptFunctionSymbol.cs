namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a user-defined function and its input parameters.
/// </summary>
public sealed class RuleScriptFunctionSymbol
{
    public RuleScriptFunctionSymbol(string name, IEnumerable<RuleScriptParameterSymbol> parameters)
        : this(name, parameters, RuleScriptValueType.Unknown)
    {
    }

    public RuleScriptFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        bool isReturnTypeNullable = false,
        bool isExported = false,
        string? documentation = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
        IsReturnTypeNullable = isReturnTypeNullable;
        IsExported = isExported;
        Documentation = documentation;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }

    public bool IsReturnTypeNullable { get; }

    public bool IsExported { get; }

    public string? Documentation { get; }
}
