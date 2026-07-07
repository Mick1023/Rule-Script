using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed class ScriptModule(string name, IReadOnlyList<Statement> statements)
{
    public string Name { get; } = name;

    public IReadOnlyList<Statement> Statements { get; } = statements;

    public Dictionary<string, List<UserFunction>> Functions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<UserFunction>> PublicFunctions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<UserFunction>> HostTriggers { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ModuleConstant> Constants { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ModuleConstant> PublicConstants { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScriptModule> Aliases { get; } = new(StringComparer.Ordinal);

    public void AddFunction(UserFunction function)
    {
        AddFunction(Functions, function);
        if (!string.IsNullOrWhiteSpace(function.Declaration.HostTriggerName))
        {
            AddFunction(HostTriggers, function, function.Declaration.HostTriggerName);
        }
    }

    public void AddPublicFunction(UserFunction function)
    {
        AddFunction(PublicFunctions, function);
    }

    private static void AddFunction(
        IDictionary<string, List<UserFunction>> functions,
        UserFunction function,
        string? name = null)
    {
        name ??= function.Declaration.Name;
        if (!functions.TryGetValue(name, out var overloads))
        {
            overloads = [];
            functions[name] = overloads;
        }

        var signatureKey = RuleScriptFunctionSymbol.CreateSignatureKey(
            name,
            CreateParameterSymbols(function.Declaration));
        var existingIndex = overloads.FindIndex(candidate =>
            RuleScriptFunctionSymbol.CreateSignatureKey(name, CreateParameterSymbols(candidate.Declaration)) == signatureKey);

        if (existingIndex >= 0)
        {
            overloads[existingIndex] = function;
            return;
        }

        overloads.Add(function);
    }

    private static IReadOnlyList<RuleScriptParameterSymbol> CreateParameterSymbols(FunctionDeclarationStatement function)
    {
        return function.ParameterDefinitions.Select(parameter =>
        {
            var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                ? parsed
                : RuleScriptValueType.Unknown;
            return new RuleScriptParameterSymbol(parameter.Name, type);
        }).ToArray();
    }
}

internal sealed record ModuleConstant(ConstStatement Declaration, ScriptModule Module);
