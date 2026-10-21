using System.Text.Json;
using System.Text.Json.Serialization;
using RuleScript.Core.Runtime;

namespace RuleScript.LanguageServer;

/// <summary>
/// Declares host-provided metadata in a workspace <c>rulescript.json</c>, so analysis knows about
/// Host Functions and context variables that the host application registers at runtime.
/// </summary>
internal sealed class RuleScriptManifest
{
    public const string FileName = "rulescript.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("hostFunctions")]
    public List<HostFunctionEntry> HostFunctions { get; init; } = [];

    [JsonPropertyName("variables")]
    public List<VariableEntry> Variables { get; init; } = [];

    public static RuleScriptManifest Parse(string json)
    {
        return JsonSerializer.Deserialize<RuleScriptManifest>(json, SerializerOptions) ?? new RuleScriptManifest();
    }

    /// <summary>
    /// Registers the declared metadata on an analysis-only engine and returns a message for each invalid entry.
    /// </summary>
    public IReadOnlyList<string> ApplyTo(RuleScriptEngine engine)
    {
        var problems = new List<string>();

        foreach (var function in HostFunctions)
        {
            if (string.IsNullOrWhiteSpace(function.Name))
            {
                problems.Add("A host function entry is missing 'name'.");
                continue;
            }

            // Host Function signatures require a known type, so missing or invalid types fall back to any.
            var parameters = new List<RuleScriptParameterSymbol>();
            foreach (var parameter in function.Parameters)
            {
                parameters.Add(new RuleScriptParameterSymbol(
                    parameter.Name ?? "value",
                    OrAny(ParseType(parameter.Type, $"{function.Name}.{parameter.Name}", problems))));
            }

            var name = function.Name;
            try
            {
                engine.RegisterFunction(
                    name,
                    _ => throw new InvalidOperationException($"Host Function '{name}' is declared in {FileName} for analysis only and cannot run in the language server."),
                    new RuleScriptHostFunctionOptions
                    {
                        Parameters = parameters,
                        ReturnType = OrAny(ParseType(function.ReturnType, $"{function.Name} return type", problems)),
                        Documentation = function.Documentation
                    });
            }
            catch (ArgumentException exception)
            {
                problems.Add($"Host function '{name}' was skipped: {exception.Message}");
            }
        }

        foreach (var variable in Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                problems.Add("A variable entry is missing 'name'.");
                continue;
            }

            engine.SetKnownVariable(variable.Name, ParseType(variable.Type, variable.Name, problems));
        }

        return problems;
    }

    private static RuleScriptValueType OrAny(RuleScriptValueType type)
    {
        return type == RuleScriptValueType.Unknown ? RuleScriptValueType.Any : type;
    }

    private static RuleScriptValueType ParseType(string? type, string owner, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return RuleScriptValueType.Unknown;
        }

        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return RuleScriptValueType.Boolean;
        }

        if (Enum.TryParse<RuleScriptValueType>(type, ignoreCase: true, out var parsed) && parsed != RuleScriptValueType.Unknown)
        {
            return parsed;
        }

        problems.Add($"Unknown type '{type}' for {owner}. Use any, null, number, string, boolean, array, object, or void.");
        return RuleScriptValueType.Unknown;
    }

    internal sealed class HostFunctionEntry
    {
        public string? Name { get; init; }

        public List<ParameterEntry> Parameters { get; init; } = [];

        public string? ReturnType { get; init; }

        public string? Documentation { get; init; }
    }

    internal sealed class ParameterEntry
    {
        public string? Name { get; init; }

        public string? Type { get; init; }
    }

    internal sealed class VariableEntry
    {
        public string? Name { get; init; }

        public string? Type { get; init; }
    }
}
