using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

[CollectionDefinition("RuntimeLimits", DisableParallelization = true)]
public sealed class RuntimeLimitsCollection
{
}

[Collection("RuntimeLimits")]
public sealed class Version120FeatureTests
{
    [Fact]
    public void TryAnalyze_UndefinedFunction_ReturnsCodedError()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("var result = MissingFunction();");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("MissingFunction", diagnostic.TokenText);
        Assert.Equal(1, diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
        Assert.NotNull(diagnostic.Range);
        Assert.False(result.Success);
    }

    [Fact]
    public void TryAnalyze_UndefinedVariable_ReturnsWarningWithoutFailingAnalysis()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("var result = ExternalValue;");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedVariable);
        Assert.Equal(RuleScriptDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(result.Success);
    }

    [Fact]
    public void SetKnownVariable_RemovesUndefinedWarningAndProvidesType()
    {
        var engine = new RuleScriptEngine();
        engine.SetKnownVariable("ExternalValue", RuleScriptValueType.Number);

        var result = engine.TryAnalyze("var result = ExternalValue + 1;");

        Assert.DoesNotContain(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedVariable);
        Assert.Equal(RuleScriptValueType.Number, Assert.Single(result.Symbols.Variables, value => value.Name == "ExternalValue").Type);
    }

    [Fact]
    public void TryAnalyze_KnownTypeMismatch_ReturnsCodedError()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("var result = \"text\" - 1;");

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.Success);
    }

    [Fact]
    public void TryAnalyze_DuplicateVariable_ReturnsCodedError()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("var value = 1; var value = 2;");

        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.DuplicateDeclaration);
        Assert.False(result.Success);
    }

    [Fact]
    public void TryAnalyze_DuplicateParameter_ReturnsSpecificDiagnosticCode()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("function Bad(value, value): return value; endfunction");

        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.DuplicateParameter);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_ExposesSemanticDiagnosticsOnSymbolResult()
    {
        var engine = new RuleScriptEngine();

        var result = engine.Analyze("var result = \"text\" - 1;");

        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TryAnalyze_TypedHostFunctionArgumentMismatch_ReturnsError()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Double",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.Number,
            arguments => Convert.ToDouble(arguments[0]) * 2);

        var result = engine.TryAnalyze("var result = Double(\"wrong\");");

        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.False(result.Success);
    }

    [Fact]
    public void TryAnalyze_TypedParameterAssignmentMismatch_ReturnsError()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("function Bad(value: number): value = \"wrong\"; endfunction");

        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.False(result.Success);
    }

    [Fact]
    public void TryAnalyze_UsesLatestScriptTextWithoutCachingDiagnostics()
    {
        var engine = new RuleScriptEngine();

        var first = engine.TryAnalyze("var result = MissingFunction();");
        var second = engine.TryAnalyze("var result = 1;");

        Assert.Contains(first.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
        Assert.Empty(second.Diagnostics);
        Assert.True(second.Success);
    }

    [Fact]
    public void Execute_StatementLimitEnabled_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine
        {
            MaxExecutedStatements = 2,
            StatementExecutionLimitEnabled = true
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("var a = 1; var b = 2; var c = 3;"));

        Assert.Contains("Statement execution limit of 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_StatementLimitDisabled_AllowsAdditionalStatements()
    {
        var engine = new RuleScriptEngine
        {
            MaxExecutedStatements = 1,
            StatementExecutionLimitEnabled = false
        };

        var context = engine.Execute("var a = 1; var b = 2; var c = 3;");

        Assert.Equal(3d, context.Get<double>("c"));
    }

    [Fact]
    public void Execute_CallDepthLimitEnabled_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine
        {
            MaxCallDepth = 1,
            CallDepthLimitEnabled = true
        };
        const string script = """
            function Second():
                return 2;
            endfunction
            function First():
                return Second();
            endfunction
            result = First();
            """;

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute(script));

        Assert.Contains("Call depth limit of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CallDepthLimitDisabled_AllowsNestedFunctions()
    {
        var engine = new RuleScriptEngine
        {
            MaxCallDepth = 1,
            CallDepthLimitEnabled = false
        };
        const string script = """
            function Second():
                return 2;
            endfunction
            function First():
                return Second();
            endfunction
            result = First();
            """;

        var context = engine.Execute(script);

        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_TimeoutEnabled_StopsLongRunningExecution()
    {
        var engine = new RuleScriptEngine
        {
            ExecutionTimeout = TimeSpan.FromMilliseconds(1),
            ExecutionTimeoutEnabled = true,
            LoopIterationLimitEnabled = false,
            StatementExecutionLimitEnabled = false
        };
        const string script = "var value = 0; while true: value = value + 1; endwhile";

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute(script));

        Assert.Contains("Execution timeout limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TimeoutDisabled_IgnoresConfiguredTimeout()
    {
        var engine = new RuleScriptEngine
        {
            ExecutionTimeout = TimeSpan.FromTicks(1),
            ExecutionTimeoutEnabled = false
        };

        var context = engine.Execute("result = 1;");

        Assert.Equal(1d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_StatementLimitMatchesSynchronousExecution()
    {
        var engine = new RuleScriptEngine
        {
            MaxExecutedStatements = 1,
            StatementExecutionLimitEnabled = true
        };

        var exception = await Assert.ThrowsAsync<RuntimeException>(() => engine.ExecuteAsync("var a = 1; var b = 2;"));

        Assert.Contains("Statement execution limit of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteFile_StatementLimitMatchesTextExecution()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["virtual:/main.rules"] = "var a = 1; var b = 2;"
        };
        var engine = new RuleScriptEngine
        {
            ImportResolver = new MutableImportResolver(files),
            WorkingDirectory = "virtual:/",
            MaxExecutedStatements = 1,
            StatementExecutionLimitEnabled = true
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.ExecuteFile("main.rules"));

        Assert.Contains("Statement execution limit of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugSession_UsesEngineExecutionLimits()
    {
        var engine = new RuleScriptEngine
        {
            MaxExecutedStatements = 1,
            StatementExecutionLimitEnabled = true
        };
        var session = new RuleScriptDebugSession(engine);

        var exception = await Assert.ThrowsAsync<RuntimeException>(() => session.RunAsync("var a = 1; var b = 2;"));

        Assert.Contains("Statement execution limit of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAnalyze_ReloadsImportedFunctionsOnEveryCall()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["virtual:/module.rules"] = "export function Current(): return 1; endfunction"
        };
        var engine = new RuleScriptEngine
        {
            ImportResolver = new MutableImportResolver(files),
            WorkingDirectory = "virtual:/"
        };
        const string script = "import \"module.rules\" as module; var result = module.Current();";

        var first = engine.TryAnalyze(script);
        files["virtual:/module.rules"] = "export function Replacement(): return 2; endfunction";
        var second = engine.TryAnalyze(script);

        Assert.DoesNotContain(first.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
        Assert.Contains(second.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
    }

    private sealed class MutableImportResolver(IReadOnlyDictionary<string, string> files) : IImportResolver
    {
        public string GetFullPath(string path)
        {
            var normalized = path.Replace('\\', '/');

            if (normalized.Contains("main.rules", StringComparison.OrdinalIgnoreCase))
            {
                return "virtual:/main.rules";
            }

            if (normalized.Contains("module.rules", StringComparison.OrdinalIgnoreCase))
            {
                return "virtual:/module.rules";
            }

            return normalized;
        }

        public bool Exists(string path) => files.ContainsKey(GetFullPath(path));

        public string ReadAllText(string path) => files[GetFullPath(path)];
    }
}
