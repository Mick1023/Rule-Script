using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed class ScriptModule(string name, IReadOnlyList<Statement> statements)
{
    public string Name { get; } = name;

    public IReadOnlyList<Statement> Statements { get; } = statements;

    public Dictionary<string, List<UserFunction>> Functions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<UserFunction>> PublicFunctions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ModuleConstant> Constants { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ModuleConstant> PublicConstants { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScriptModule> Aliases { get; } = new(StringComparer.Ordinal);

    public void AddFunction(UserFunction function)
    {
        AddFunction(Functions, function);
    }

    public void AddPublicFunction(UserFunction function)
    {
        AddFunction(PublicFunctions, function);
    }

    private static void AddFunction(IDictionary<string, List<UserFunction>> functions, UserFunction function)
    {
        if (!functions.TryGetValue(function.Declaration.Name, out var overloads))
        {
            overloads = [];
            functions[function.Declaration.Name] = overloads;
        }

        var signatureKey = RuleScriptFunctionSymbol.CreateSignatureKey(
            function.Declaration.Name,
            CreateParameterSymbols(function.Declaration));
        var existingIndex = overloads.FindIndex(candidate =>
            RuleScriptFunctionSymbol.CreateSignatureKey(candidate.Declaration.Name, CreateParameterSymbols(candidate.Declaration)) == signatureKey);

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
