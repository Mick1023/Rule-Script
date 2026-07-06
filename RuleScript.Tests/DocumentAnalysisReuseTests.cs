using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class DocumentAnalysisReuseTests
{
    [Fact]
    public void AnalyzeDocument_ReturnsReusableParseAndAnalysisResult()
    {
        const string source = """
            #region Main
            /// Adds one.
            function AddOne(value: number):
                return value + 1;
            endfunction
            #endregion

            AddOne(1);
            """;

        var document = RuleScriptLanguageService.AnalyzeDocument(source);

        Assert.NotEmpty(document.Tokens);
        Assert.NotEmpty(document.Statements);
        Assert.Single(document.Regions);
        Assert.Contains(document.Analysis.Functions, function => function.Name == "AddOne");

        var definition = RuleScriptLanguageService.GetDefinition(document, 8, 1);
        var references = RuleScriptLanguageService.FindReferences(document, 8, 1);

        Assert.NotNull(definition);
        Assert.Equal("AddOne", definition.Name);
        Assert.Equal(2, references.Count);
    }

    [Fact]
    public void AnalyzeDocument_WithEngine_ReusesHostFunctionMetadataForNavigation()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "HostScore",
            _ => 42,
            new RuleScriptHostFunctionOptions
            {
                Parameters = [new RuleScriptParameterSymbol("playerId", RuleScriptValueType.String)],
                ReturnType = RuleScriptValueType.Number,
                Documentation = "Reads the player's score."
            });
        const string source = """HostScore("p1");""";

        var document = RuleScriptLanguageService.AnalyzeDocument(engine, source);
        var definition = RuleScriptLanguageService.GetDefinition(document, 1, 1);

        Assert.NotNull(definition);
        Assert.Equal("HostScore", definition.Name);
        Assert.Equal(RuleScriptValueType.Number, definition.ReturnType);
        Assert.Single(document.Analysis.Functions, function => function.Name == "HostScore" && function.Kind == RuleScriptFunctionKind.Host);
    }

    [Fact]
    public void AnalysisCache_ReusesUnchangedDocumentsAndUpdatesSingleDocument()
    {
        var cache = new RuleScriptAnalysisCache();

        var firstMain = cache.GetOrAnalyze("main.rules", "value = 1;");
        var secondMain = cache.GetOrAnalyze("main.rules", "value = 1;");
        var library = cache.GetOrAnalyze("library.rules", "function Lib():\nendfunction");
        var updatedMain = cache.GetOrAnalyze("main.rules", "value = 2;");

        Assert.Same(firstMain, secondMain);
        Assert.NotSame(firstMain, updatedMain);
        Assert.True(cache.TryGetDocument("library.rules", out var cachedLibrary));
        Assert.Same(library, cachedLibrary);
    }
}
