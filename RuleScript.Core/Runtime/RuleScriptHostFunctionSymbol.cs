namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a registered host function signature.
/// </summary>
[Obsolete("Use RuleScriptFunctionSymbol with Kind == RuleScriptFunctionKind.Host.")]
public sealed class RuleScriptHostFunctionSymbol
{
    public RuleScriptHostFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        bool isAsync = false,
        bool isThreadSafe = false,
        bool isVariadic = false,
        string? documentation = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
        IsAsync = isAsync;
        IsThreadSafe = isThreadSafe;
        IsVariadic = isVariadic;
        Documentation = documentation;
        Function = CreateFunctionSymbol();
    }

    internal RuleScriptHostFunctionSymbol(RuleScriptFunctionSymbol symbol)
        : this(
            symbol.Name,
            symbol.Parameters,
            symbol.ReturnType,
            symbol.HostMetadata?.IsAsync ?? false,
            symbol.HostMetadata?.IsThreadSafe ?? false,
            symbol.HostMetadata?.IsVariadic ?? false,
            symbol.Documentation)
    {
        Function = symbol;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }

    public bool IsAsync { get; }

    public bool IsThreadSafe { get; }

    public bool IsVariadic { get; }

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
            Documentation,
            RuleScriptFunctionKind.Host,
            hostMetadata: new RuleScriptHostFunctionMetadata(IsAsync, IsThreadSafe, IsVariadic));
    }
}
