namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a user-defined function and its input parameters.
/// </summary>
public sealed class RuleScriptFunctionSymbol
{
    public RuleScriptFunctionSymbol(string name, IEnumerable<RuleScriptParameterSymbol> parameters)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        Name = name;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
    }

    public string Name { get; }

    public IReadOnlyList<RuleScriptParameterSymbol> Parameters { get; }
}
