using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Formatting;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class MaintenanceRefactorRegressionTests
{
    [Fact]
    public void Navigation_ReturnsNullOrEmptyForMissingSymbol()
    {
        const string source = """
            var value = 1;

            value = value + 1;
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 2, 1);
        var references = RuleScriptLanguageService.FindReferences(source, 2, 1);

        Assert.Null(definition);
        Assert.Empty(references);
    }

    [Fact]
    public void PublicApiCompatibility_LegacyHostFunctionsRemainBackedByUnifiedSymbols()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Read",
            [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            args => $"item-{args[0]}",
            threadSafe: true);

#pragma warning disable CS0618
        var legacy = Assert.Single(engine.RegisteredHostFunctions);
#pragma warning restore CS0618
        var unified = Assert.Single(engine.RegisteredFunctionSymbols);

        Assert.Equal("Read", legacy.Name);
        Assert.Equal(RuleScriptFunctionKind.Host, legacy.Function.Kind);
        Assert.Same(unified, legacy.Function);
        Assert.Same(unified, legacy.ToFunctionSymbol());
    }

    [Fact]
    public void RuntimeFormatterAndDiagnostics_RemainStableAfterMaintenanceRefactor()
    {
        const string source = """
            function Format(value):
            return ToString(value);
            endfunction

            result=Format(123);
            """;
        const string expectedFormatted = """
            function Format(value):
                return ToString(value);
            endfunction

            result = Format(123);

            """;
        var engine = new RuleScriptEngine();

        var context = engine.Execute(source);
        var formatted = RuleScriptFormatter.Format(source);
        var diagnostic = Assert.Single(
            engine.TryAnalyze("result = Missing(1);").Diagnostics,
            item => item.Code == RuleScriptDiagnosticCodes.UndefinedFunction);

        Assert.Equal("123", context.Get<string>("result"));
        Assert.Equal(NormalizeLineEndings(expectedFormatted), NormalizeLineEndings(formatted));
        Assert.Equal("Function 'Missing' is not defined.", diagnostic.Message);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
