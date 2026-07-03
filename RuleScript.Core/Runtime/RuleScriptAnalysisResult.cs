namespace RuleScript.Core.Runtime;

/// <summary>
/// Represents static script symbols collected for editor tooling.
/// </summary>
public sealed class RuleScriptAnalysisResult
{
    internal RuleScriptAnalysisResult(
        IEnumerable<string> variableNames,
        IEnumerable<string> userFunctionNames,
        IEnumerable<string> hostFunctionNames,
        IEnumerable<string> builtinFunctionNames,
        IEnumerable<string> importAliases,
        IEnumerable<RuleScriptVariableSymbol>? variables = null,
        IEnumerable<RuleScriptFunctionSymbol>? userFunctions = null,
        IEnumerable<RuleScriptVariableSymbol>? visibleVariables = null,
        IEnumerable<RuleScriptHostFunctionSymbol>? hostFunctions = null,
        IEnumerable<RuleScriptDiagnostic>? diagnostics = null,
        IEnumerable<RuleScriptBuiltinFunctionSymbol>? builtinFunctions = null)
    {
        VariableNames = Snapshot(variableNames);
        UserFunctionNames = Snapshot(userFunctionNames);
        HostFunctionNames = Snapshot(hostFunctionNames);
        BuiltinFunctionNames = Snapshot(builtinFunctionNames);
        FunctionNames = Snapshot(UserFunctionNames.Concat(HostFunctionNames).Concat(BuiltinFunctionNames));
        ImportAliases = Snapshot(importAliases);
        Variables = SnapshotVariables(variables, VariableNames);
        UserFunctions = SnapshotFunctions(userFunctions, UserFunctionNames);
        VisibleVariables = visibleVariables is null
            ? Variables
            : SnapshotVariables(visibleVariables, []);
        VisibleVariableNames = Snapshot(VisibleVariables.Select(variable => variable.Name));
        HostFunctions = SnapshotHostFunctions(hostFunctions);
        BuiltinFunctions = SnapshotBuiltinFunctions(builtinFunctions);
        Diagnostics = diagnostics?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets variable names declared or assigned in the script.
    /// </summary>
    public IReadOnlyList<string> VariableNames { get; }

    /// <summary>
    /// Gets variables and their best-effort inferred or declared types.
    /// </summary>
    public IReadOnlyList<RuleScriptVariableSymbol> Variables { get; }

    /// <summary>
    /// Gets variables visible at the requested cursor position. Without a cursor, this contains every analyzed variable.
    /// </summary>
    public IReadOnlyList<RuleScriptVariableSymbol> VisibleVariables { get; }

    /// <summary>
    /// Gets variable names visible at the requested cursor position.
    /// </summary>
    public IReadOnlyList<string> VisibleVariableNames { get; }

    /// <summary>
    /// Gets user-defined function names declared in the script or exposed by imports. Alias-imported names are qualified with the alias.
    /// </summary>
    public IReadOnlyList<string> UserFunctionNames { get; }

    /// <summary>
    /// Gets user-defined function signatures, including input parameter names and types.
    /// </summary>
    public IReadOnlyList<RuleScriptFunctionSymbol> UserFunctions { get; }

    /// <summary>
    /// Gets host function names currently registered on the engine.
    /// </summary>
    public IReadOnlyList<string> HostFunctionNames { get; }

    /// <summary>
    /// Gets typed host function signatures registered on the engine.
    /// Legacy registrations without metadata remain available through <see cref="HostFunctionNames"/>.
    /// </summary>
    public IReadOnlyList<RuleScriptHostFunctionSymbol> HostFunctions { get; }

    /// <summary>
    /// Gets built-in function names available to the script.
    /// </summary>
    public IReadOnlyList<string> BuiltinFunctionNames { get; }

    /// <summary>
    /// Gets typed signatures for built-in functions that expose analysis metadata.
    /// </summary>
    public IReadOnlyList<RuleScriptBuiltinFunctionSymbol> BuiltinFunctions { get; }

    /// <summary>
    /// Gets all callable function names available from the current script and engine.
    /// </summary>
    public IReadOnlyList<string> FunctionNames { get; }

    /// <summary>
    /// Gets import aliases declared in the script.
    /// </summary>
    public IReadOnlyList<string> ImportAliases { get; }

    /// <summary>
    /// Gets semantic diagnostics produced from the current script text.
    /// </summary>
    public IReadOnlyList<RuleScriptDiagnostic> Diagnostics { get; }

    private static IReadOnlyList<string> Snapshot(IEnumerable<string> names)
    {
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RuleScriptVariableSymbol> SnapshotVariables(
        IEnumerable<RuleScriptVariableSymbol>? symbols,
        IEnumerable<string> fallbackNames)
    {
        var byName = new Dictionary<string, RuleScriptVariableSymbol>(StringComparer.Ordinal);

        if (symbols is not null)
        {
            foreach (var symbol in symbols)
            {
                byName[symbol.Name] = symbol;
            }
        }

        foreach (var name in fallbackNames)
        {
            byName.TryAdd(name, new RuleScriptVariableSymbol(name, RuleScriptValueType.Unknown));
        }

        return byName.Values.OrderBy(symbol => symbol.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<RuleScriptFunctionSymbol> SnapshotFunctions(
        IEnumerable<RuleScriptFunctionSymbol>? symbols,
        IEnumerable<string> fallbackNames)
    {
        var byName = new Dictionary<string, RuleScriptFunctionSymbol>(StringComparer.Ordinal);

        if (symbols is not null)
        {
            foreach (var symbol in symbols)
            {
                byName[symbol.Name] = new RuleScriptFunctionSymbol(
                    symbol.Name,
                    symbol.Parameters,
                    symbol.ReturnType,
                    symbol.IsReturnTypeNullable,
                    symbol.IsExported,
                    symbol.Documentation);
            }
        }

        foreach (var name in fallbackNames)
        {
            byName.TryAdd(name, new RuleScriptFunctionSymbol(name, []));
        }

        return byName.Values.OrderBy(symbol => symbol.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<RuleScriptHostFunctionSymbol> SnapshotHostFunctions(
        IEnumerable<RuleScriptHostFunctionSymbol>? symbols)
    {
        return symbols?
            .Select(symbol => new RuleScriptHostFunctionSymbol(
                symbol.Name,
                symbol.Parameters,
                symbol.ReturnType,
                symbol.IsAsync,
                symbol.IsThreadSafe,
                symbol.IsVariadic))
            .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.IsAsync)
            .ToArray()
            ?? [];
    }

    private static IReadOnlyList<RuleScriptBuiltinFunctionSymbol> SnapshotBuiltinFunctions(
        IEnumerable<RuleScriptBuiltinFunctionSymbol>? symbols)
    {
        return symbols?
            .Select(symbol => new RuleScriptBuiltinFunctionSymbol(
                symbol.Name,
                symbol.Parameters,
                symbol.ReturnType))
            .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToArray()
            ?? [];
    }
}
