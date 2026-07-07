using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class HostTriggerTests
{
    [Fact]
    public void Lexer_RecognizesHostTriggerAttributeAndTriggerKeyword()
    {
        var tokens = new Lexer("""
            @HostTrigger("OnScan")
            function Handle(): endfunction
            parallel:
                trigger task:
                    dispatch;
                endtask
            endparallel
            """).Tokenize();

        Assert.Contains(tokens, token => token.Type == TokenType.At);
        Assert.Contains(tokens, token => token.Type == TokenType.Trigger);
        Assert.Contains(tokens, token => token.Type == TokenType.String && Equals(token.Literal, "OnScan"));
    }

    [Fact]
    public void Parser_ParsesHostTriggerMetadataOnFunction()
    {
        var function = Assert.IsType<FunctionDeclarationStatement>(Assert.Single(Parse("""
            @HostTrigger("OnScan")
            function Handle(id: string):
                return id;
            endfunction
            """)));

        Assert.Equal("Handle", function.Name);
        Assert.Equal("OnScan", function.HostTriggerName);
        Assert.Equal("id", Assert.Single(function.Parameters));
    }

    [Fact]
    public void Parser_ParsesTriggerTaskInsideParallel()
    {
        var statement = Assert.IsType<ParallelStatementSyntax>(Assert.Single(Parse("""
            parallel:
                trigger task:
                    dispatch;
                endtask
            endparallel
            """)));

        var task = Assert.Single(statement.Tasks);
        Assert.Equal(TaskBlockKind.Trigger, task.Kind);
        Assert.Single(task.Body);
    }

    [Fact]
    public void Parser_RejectsTriggerTaskOutsideParallel()
    {
        var exception = Assert.Throws<SyntaxException>(() => Parse("""
            trigger task:
                dispatch;
            endtask
            """));

        Assert.Contains("only allowed", exception.Message);
    }

    [Fact]
    public void Analyze_ReturnsHostTriggerMetadataForMarkedFunction()
    {
        var result = new RuleScriptEngine().Analyze("""
            @HostTrigger("OnScan")
            function Handle(id: string):
                return id;
            endfunction
            """);

        var trigger = Assert.Single(result.HostTriggers);
        Assert.Equal("Handle", trigger.Name);
        Assert.Equal("OnScan", trigger.HostTriggerMetadata?.Name);
        Assert.Equal(RuleScriptValueType.String, Assert.Single(trigger.Parameters).Type);
    }

    [Fact]
    public void Analyze_DoesNotReportDispatchAsUndefinedVariableInTriggerTask()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            parallel:
                trigger task:
                    dispatch;
                endtask
            endparallel
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedVariable
            && diagnostic.TokenText == "dispatch");
    }

    [Fact]
    public void Runtime_HostTriggers_ReturnsMarkedFunctionSymbols()
    {
        var runtime = new RuleScriptEngine().CreateRuntime("""
            @HostTrigger("OnScan")
            function Handle(id: string):
                return id;
            endfunction
            """);

        var trigger = Assert.Single(runtime.HostTriggers);
        Assert.Equal("Handle", trigger.Name);
        Assert.Equal("OnScan", trigger.HostTriggerMetadata?.Name);
        Assert.Equal(RuleScriptValueType.String, Assert.Single(trigger.Parameters).Type);
    }

    [Fact]
    public void Runtime_CanBeCreatedFromFileUsingEngineWorkingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rulescript-hosttrigger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "main.rules"), """
                @HostTrigger("OnScan")
                function Handle(id: string):
                    return id;
                endfunction
                """);
            var engine = new RuleScriptEngine
            {
                WorkingDirectory = directory
            };

            var runtime = engine.CreateRuntimeFromFile("main");

            var trigger = Assert.Single(runtime.HostTriggers);
            Assert.Equal("Handle", trigger.Name);
            Assert.Equal("OnScan", trigger.HostTriggerMetadata?.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HostTriggerQueueProcessesEventsInFifoOrder()
    {
        var observed = new List<double>();
        var engine = new RuleScriptEngine { ExecutionTimeoutEnabled = false };
        engine.RegisterFunction(
            "Record",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.Null,
            args =>
            {
                lock (observed)
                {
                    observed.Add((double)args[0]!);
                }

                return null;
            },
            threadSafe: true);
        var runtime = engine.CreateRuntime("""
            @HostTrigger("OnEvent")
            function Handle(value: number):
                Record(value);
            endfunction

            parallel:
                trigger task:
                    dispatch;
                endtask
            endparallel
            """);

        var execution = runtime.StartAsync();
        await runtime.TriggerAsync("OnEvent", [1d]);
        await runtime.TriggerAsync("OnEvent", [2d]);
        await runtime.TriggerAsync("OnEvent", [3d]);

        await WaitForAsync(() =>
        {
            lock (observed)
            {
                return observed.Count == 3;
            }
        });
        await runtime.StopAsync();
        await execution;

        Assert.Equal([1d, 2d, 3d], observed);
    }

    [Fact]
    public async Task Runtime_RemainsRunningAfterDispatchingSingleHostTrigger()
    {
        var observed = new List<string>();
        var engine = new RuleScriptEngine { ExecutionTimeoutEnabled = false };
        engine.RuntimeEventHandlerAsync = (runtimeEvent, _) =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print)
            {
                lock (observed)
                {
                    observed.Add((string)runtimeEvent.Value!);
                }
            }

            return Task.FromResult(RuleScriptExecutionDirective.Continue);
        };
        var runtime = engine.CreateRuntime("""
            parallel:
                trigger task:
                    dispatch;
                end
            end

            @HostTrigger("HostPrint")
            function HostPrint(msg: string):
                Print(msg);
            end
            """);

        var execution = runtime.StartAsync();
        await runtime.TriggerAsync("HostPrint", ["hello"]);

        await WaitForAsync(() =>
        {
            lock (observed)
            {
                return observed.Count == 1;
            }
        });
        await Task.Delay(100);

        Assert.Equal(RuleScriptRuntimeState.Running, runtime.State);
        Assert.False(execution.IsCompleted);

        await runtime.StopAsync();
        await execution;
    }

    [Fact]
    public async Task Runtime_HostCannotTriggerUnmarkedFunction()
    {
        var engine = new RuleScriptEngine { ExecutionTimeoutEnabled = false };
        var runtime = engine.CreateRuntime("""
            function Internal():
                return 1;
            endfunction

            parallel:
                trigger task:
                    dispatch;
                endtask
            endparallel
            """);

        var execution = runtime.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.TriggerAsync("Internal"));
        await runtime.StopAsync();
        await execution;
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
    }
}
