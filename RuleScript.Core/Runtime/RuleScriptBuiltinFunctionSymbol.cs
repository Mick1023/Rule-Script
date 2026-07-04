namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a built-in function signature available to RuleScript analysis tools.
/// </summary>
[Obsolete("Use RuleScriptFunctionSymbol with Kind == RuleScriptFunctionKind.Builtin.")]
public sealed class RuleScriptBuiltinFunctionSymbol
{
    public RuleScriptBuiltinFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        string? documentation = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
        Documentation = documentation;
        Function = CreateFunctionSymbol();
    }

    internal RuleScriptBuiltinFunctionSymbol(RuleScriptFunctionSymbol symbol)
        : this(symbol.Name, symbol.Parameters, symbol.ReturnType, symbol.Documentation)
    {
        Function = symbol;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }

    public string? Documentation { get; }

    public RuleScriptFunctionSymbol Function { get; }

    public RuleScriptFunctionSymbol ToFunctionSymbol()
    {
        return Function;
    }

    private RuleScriptFunctionSymbol CreateFunctionSymbol()
    {
        return new RuleScriptFunctionSymbol(
            Name,
            Parameters,
            ReturnType,
            isReturnTypeNullable: false,
            isExported: false,
            documentation: Documentation,
            kind: RuleScriptFunctionKind.Builtin,
            builtinMetadata: new RuleScriptBuiltinFunctionMetadata());
    }
}
