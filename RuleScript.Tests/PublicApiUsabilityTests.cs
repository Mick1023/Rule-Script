using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class PublicApiUsabilityTests
{
    [Fact]
    public void Execute_ReturnsRuntimeContext()
    {
        var engine = new RuleScriptEngine();

        var context = engine.Execute("result = 123;");

        Assert.Equal(123d, context.Get<double>("result"));
    }

    [Fact]
    public void RuntimeContext_TryGet_ReturnsStoredValue()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        var found = context.TryGet("Name", out var value);

        Assert.True(found);
        Assert.Equal("Mick", value);
    }

    [Fact]
    public void RuntimeContext_ContainsAndGetOrDefault_Work()
    {
        var context = new RuntimeContext();
        context.Set("Age", 30);

        Assert.True(context.Contains("Age"));
        Assert.Equal(30, context.GetOrDefault<int>("Age"));
        Assert.Equal("fallback", context.GetOrDefault("Missing", "fallback"));
    }

    [Fact]
    public void RuntimeContext_Clear_RemovesVariables()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        context.Clear();

        Assert.False(context.Contains("Name"));
    }

    [Fact]
    public void RuntimeContext_VariablesSnapshot_CannotMutateInternalState()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        var snapshot = context.Variables;
        Assert.Throws<NotSupportedException>(() =>
        {
            var mutable = (IDictionary<string, RuntimeValue>)snapshot;
            mutable["Name"] = new RuntimeValue("Changed");
        });

        Assert.Equal("Mick", context.Get<string>("Name"));
    }

    [Fact]
    public void RuntimeContext_VariableNames_ReturnsSortedSnapshot()
    {
        var context = new RuntimeContext();
        context.Set("beta", 2);
        context.Set("alpha", 1);

        var names = context.VariableNames;
        context.Set("gamma", 3);

        Assert.Equal(["alpha", "beta"], names);
        Assert.Equal(["alpha", "beta", "gamma"], context.VariableNames);
    }

    [Fact]
    public void RuleScriptEngine_GetVariableNames_ReturnsContextVariableNames()
    {
        var engine = new RuleScriptEngine();
        var context = engine.Execute("""
            var distance = 519;

            function LocalValue():
                var localOnly = 1;
                return localOnly;
            endfunction

            result = LocalValue();
            """);

        Assert.Equal(["distance", "result"], engine.GetVariableNames(context));
    }

    [Fact]
    public void RegisterFunction_OverwriteBehavior_RemainsStable()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => "first");
        engine.RegisterFunction("Value", _ => "second");

        var context = engine.Execute("result = Value();");

        Assert.Equal("second", context.Get<string>("result"));
    }

    [Fact]
    public void RegisteredFunctionNames_ReturnsSyncAndAsyncHostFunctionNames()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ => 1);
        engine.RegisterFunctionAsync("Delay", _ => Task.FromResult<object?>(null));

        Assert.Equal(["Delay", "Read"], engine.RegisteredFunctionNames);
    }

    [Fact]
    public void RegisteredFunctionNames_ReflectsOverwriteUnregisterAndClear()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);
        engine.RegisterFunctionAsync("Value", _ => Task.FromResult<object?>(2));
        engine.RegisterFunction("Other", _ => 3);

        Assert.Equal(["Other", "Value"], engine.RegisteredFunctionNames);

        Assert.True(engine.UnregisterFunction("Value"));
        Assert.Equal(["Other"], engine.RegisteredFunctionNames);

        engine.ClearFunctions();
        Assert.Empty(engine.RegisteredFunctionNames);
    }

    [Fact]
    public void Analyze_ReturnsStaticSymbolsWithoutExecutingScript()
    {
        var engine = new RuleScriptEngine();
        var called = false;
        engine.RegisterFunction("Read", _ =>
        {
            called = true;
            return 1;
        });
        engine.RegisterFunctionAsync("Wait", _ => Task.FromResult<object?>(null));

        var result = engine.Analyze("""
            import "robot.rules" as robot;

            var distance = Read();
            status = "OK";

            foreach item in [1, 2]:
                total = item;
            endforeach

            function Format(value):
                var localText = ToString(value);
                return localText;
            endfunction
            """);

        Assert.False(called);
        Assert.Equal(["distance", "item", "localText", "status", "total", "value"], result.VariableNames);
        Assert.Equal(["Format"], result.UserFunctionNames);
        Assert.Equal(["Read", "Wait"], result.HostFunctionNames);
        Assert.Contains("ToString", result.BuiltinFunctionNames);
        Assert.Contains("Format", result.FunctionNames);
        Assert.Contains("Read", result.FunctionNames);
        Assert.Contains("ToString", result.FunctionNames);
        Assert.Equal(["robot"], result.ImportAliases);
    }

    [Fact]
    public void Analyze_IncludesFunctionsFromGlobalAndAliasImports()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rulescript-analysis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "common.rules"), "function Shared(): return 1; endfunction");
            File.WriteAllText(Path.Combine(directory, "robot.rules"), "function Read(): return 2; endfunction");

            var engine = new RuleScriptEngine { WorkingDirectory = directory };
            var result = engine.Analyze("""
                import "common.rules";
                import "robot.rules" as robot;
                """);

            Assert.Contains("Shared", result.UserFunctionNames);
            Assert.Contains("robot.Read", result.UserFunctionNames);
            Assert.Contains("Shared", result.FunctionNames);
            Assert.Contains("robot.Read", result.FunctionNames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryAnalyze_IncompleteScript_IncludesImportedFunctions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rulescript-analysis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "common.rules"), "function Shared(): return 1; endfunction");

            var engine = new RuleScriptEngine { WorkingDirectory = directory };
            var result = engine.TryAnalyze("""
                import "common.rules";
                var unfinished =
                """);

            Assert.False(result.Success);
            Assert.Contains("Shared", result.Symbols.FunctionNames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryAnalyze_ParseableScript_ReturnsSuccessfulAnalysis()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("var value = 1;");

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["value"], result.Symbols.VariableNames);
    }

    [Fact]
    public void TryAnalyze_IncompleteScript_ReturnsDiagnosticsAndPartialSymbols()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("""
            var distance =
            status = "editing";

            function Format(value):
                var localText =
            """);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains("distance", result.Symbols.VariableNames);
        Assert.Contains("status", result.Symbols.VariableNames);
        Assert.Contains("value", result.Symbols.VariableNames);
        Assert.Contains("localText", result.Symbols.VariableNames);
        Assert.Equal(["Format"], result.Symbols.UserFunctionNames);
    }

    [Fact]
    public void TryAnalyze_LexerError_ReturnsDiagnosticsAndTextScannedSymbols()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("""
            import "module.rules" as module;
            var name = "unfinished
            result = name;
            """);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains("name", result.Symbols.VariableNames);
        Assert.Contains("result", result.Symbols.VariableNames);
        Assert.Equal(["module"], result.Symbols.ImportAliases);
    }

    [Fact]
    public void TryAnalyze_ParseableScript_DoesNotPreventLaterExecute()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("""
            var value = 1;
            result = value + 1;
            """);

        var context = engine.Execute("""
            var value = 1;
            result = value + 1;
            """);

        Assert.True(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public void TryAnalyze_IncompleteScript_DoesNotPreventLaterExecute()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("var value =");

        var context = engine.Execute("""
            var value = 1;
            result = value + 1;
            """);

        Assert.False(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public void TryAnalyze_LexerError_DoesNotPreventLaterExecute()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("var value = \"unfinished");

        var context = engine.Execute("""
            var value = 1;
            result = value + 1;
            """);

        Assert.False(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task TryAnalyze_ParseableScript_DoesNotPreventLaterExecuteAsync()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("""
            var value = 1;
            result = value + 1;
            """);

        var context = await engine.ExecuteAsync("""
            var value = 1;
            result = value + 1;
            """);

        Assert.True(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task TryAnalyze_IncompleteScript_DoesNotPreventLaterExecuteAsync()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("var value =");

        var context = await engine.ExecuteAsync("""
            var value = 1;
            result = value + 1;
            """);

        Assert.False(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task TryAnalyze_LexerError_DoesNotPreventLaterExecuteAsync()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("var value = \"unfinished");

        var context = await engine.ExecuteAsync("""
            var value = 1;
            result = value + 1;
            """);

        Assert.False(analysis.Success);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public void UnregisterFunction_RemovesHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);

        Assert.True(engine.UnregisterFunction("Value"));

        Assert.Throws<RuntimeException>(() => engine.Execute("result = Value();"));
    }

    [Fact]
    public void ClearFunctions_RemovesAllHostFunctions()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("A", _ => 1);
        engine.RegisterFunction("B", _ => 2);

        engine.ClearFunctions();

        Assert.Throws<RuntimeException>(() => engine.Execute("result = A();"));
        Assert.Throws<RuntimeException>(() => engine.Execute("result = B();"));
    }
}
