namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a function and its editor-facing metadata.
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
        string? documentation = null,
        RuleScriptValueType? declaredReturnType = null,
        bool isReturnTypeDeclared = false)
        : this(
            name,
            parameters,
            returnType,
            isReturnTypeNullable,
            isExported,
            documentation,
            RuleScriptFunctionKind.User,
            declaredReturnType: declaredReturnType,
            isReturnTypeDeclared: isReturnTypeDeclared)
    {
    }

    public RuleScriptFunctionSymbol(
        string name,
        IEnumerable<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        bool isReturnTypeNullable,
        bool isExported,
        string? documentation,
        RuleScriptFunctionKind kind,
        RuleScriptSourceLocation? location = null,
        RuleScriptSourceRange? range = null,
        RuleScriptHostFunctionMetadata? hostMetadata = null,
        RuleScriptBuiltinFunctionMetadata? builtinMetadata = null,
        RuleScriptImportFunctionMetadata? importMetadata = null,
        RuleScriptValueType? declaredReturnType = null,
        bool isReturnTypeDeclared = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        ReturnType = returnType;
        DeclaredReturnType = declaredReturnType ?? returnType;
        IsReturnTypeDeclared = isReturnTypeDeclared;
        IsReturnTypeNullable = isReturnTypeNullable;
        IsExported = isExported;
        Documentation = documentation;
        Kind = kind;
        Location = location;
        Range = range;
        HostMetadata = hostMetadata;
        BuiltinMetadata = builtinMetadata;
        ImportMetadata = importMetadata;
        Metadata = hostMetadata ?? (object?)builtinMetadata ?? importMetadata;
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }

    public RuleScriptValueType ReturnType { get; }

    public RuleScriptValueType DeclaredReturnType { get; }

    public bool IsReturnTypeDeclared { get; }

    public bool IsReturnTypeNullable { get; }

    public bool IsExported { get; }

    public string? Documentation { get; }

    public RuleScriptFunctionKind Kind { get; }

    public RuleScriptSourceLocation? Location { get; }

    public RuleScriptSourceRange? Range { get; }

    public RuleScriptHostFunctionMetadata? HostMetadata { get; }

    public RuleScriptBuiltinFunctionMetadata? BuiltinMetadata { get; }

    public RuleScriptImportFunctionMetadata? ImportMetadata { get; }

    public object? Metadata { get; }
}
