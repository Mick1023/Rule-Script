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
    /// Parses and analyzes a document once so multiple editor features can reuse the same result.
    /// </summary>
    public static RuleScriptDocumentAnalysisResult AnalyzeDocument(string source)
    {
        return AnalyzeDocument(new RuleScriptEngine(), source);
    }

    /// <summary>
    /// Parses and analyzes a document once using the supplied engine metadata.
    /// </summary>
    public static RuleScriptDocumentAnalysisResult AnalyzeDocument(RuleScriptEngine engine, string source)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new Lexer.Lexer(source).Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var analysis = engine.AnalyzeParsedDocument(statements);
        return new RuleScriptDocumentAnalysisResult(engine, source, tokens, statements, parser.Regions, analysis);
    }

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

        return GetDefinition(AnalyzeDocument(engine, source), line, column);
    }

    /// <summary>
    /// Gets the definition metadata for the symbol at the requested 1-based source position using a reusable document analysis.
    /// </summary>
    public static RuleScriptDefinitionInfo? GetDefinition(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(document);

        return RuleScriptNavigationAnalyzer.GetDefinition(document, line, column);
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

        return FindReferences(AnalyzeDocument(engine, source), line, column);
    }

    /// <summary>
    /// Finds declaration and usage metadata for the symbol at the requested 1-based source position using a reusable document analysis.
    /// </summary>
    public static IReadOnlyList<RuleScriptReferenceInfo> FindReferences(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(document);

        return RuleScriptNavigationAnalyzer.FindReferences(document, line, column);
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

        return AnalyzeDocument(source)
            .Analysis
            .UserFunctions
            .LastOrDefault(function => string.Equals(function.Name, functionName, StringComparison.Ordinal))
            ?.Documentation;
    }
}
