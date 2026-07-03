using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

/// <summary>
/// Provides source metadata used by Rule Script editor integrations.
/// </summary>
public static class RuleScriptLanguageService
{
    /// <summary>
    /// Gets the definition metadata for the symbol at the requested 1-based source position.
    /// </summary>
    public static RuleScriptDefinitionInfo? GetDefinition(string source, int line, int column)
    {
        return GetDefinition(new RuleScriptEngine(), source, line, column);
    }

    /// <summary>
    /// Gets the definition metadata for the symbol at the requested 1-based source position using the supplied engine metadata.
    /// </summary>
    public static RuleScriptDefinitionInfo? GetDefinition(RuleScriptEngine engine, string source, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(source);

        return RuleScriptNavigationAnalyzer.GetDefinition(engine, source, line, column);
    }

    /// <summary>
    /// Finds declaration and usage metadata for the symbol at the requested 1-based source position.
    /// </summary>
    public static IReadOnlyList<RuleScriptReferenceInfo> FindReferences(string source, int line, int column)
    {
        return FindReferences(new RuleScriptEngine(), source, line, column);
    }

    /// <summary>
    /// Finds declaration and usage metadata for the symbol at the requested 1-based source position using the supplied engine metadata.
    /// </summary>
    public static IReadOnlyList<RuleScriptReferenceInfo> FindReferences(RuleScriptEngine engine, string source, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(source);

        return RuleScriptNavigationAnalyzer.FindReferences(engine, source, line, column);
    }

    /// <summary>
    /// Parses named region directives and returns their folding ranges.
    /// </summary>
    public static IReadOnlyList<RuleScriptRegion> GetRegions(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var parser = new Parser.Parser(new Lexer.Lexer(source).Tokenize());
        _ = parser.Parse();
        return parser.Regions;
    }

    /// <summary>
    /// Gets documentation associated with a user-defined function, or null when none is available.
    /// </summary>
    public static string? GetFunctionDocumentation(string source, string functionName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        return new RuleScriptEngine()
            .Analyze(source)
            .UserFunctions
            .LastOrDefault(function => string.Equals(function.Name, functionName, StringComparison.Ordinal))
            ?.Documentation;
    }
}
