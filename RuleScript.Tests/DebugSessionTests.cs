using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class DebugSessionTests
{
    [Fact]
    public async Task RunFileAsync_UsesSelectedFolderWorkingDirectoryForMainRules()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """
            var value = 40;
            result = value + 2;
            """);
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };
        var session = new RuleScriptDebugSession(engine);

        var context = await session.RunFileAsync("main.rules");

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public async Task Breakpoint_PausesBeforeStatementAndContinueCompletesExecution()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """
            var value = 1;
            value = 2;
            result = value;
            """);
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };
        engine.AddBreakpoint("main.rules", 2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunFileAsync("main.rules");
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.True(session.IsPaused);
        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.Equal(2, pause.Location.Line);
        Assert.False(session.Context?.Contains("result"));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.False(session.IsPaused);
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task Breakpoint_PausesWhenEngineHasAsyncRuntimeEventHandler()
    {
        var asyncHandlerEvents = new List<RuleScriptRuntimeEventKind>();
        var engine = new RuleScriptEngine();
        engine.RuntimeEventHandlerAsync = async (runtimeEvent, _) =>
        {
            await Task.Yield();
            asyncHandlerEvents.Add(runtimeEvent.Kind);
            return RuleScriptExecutionDirective.Continue;
        };
        engine.AddBreakpoint(5);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var a = 1;
            Print(a);
            var b = 2;
            Print(b);
            result = a + b;
            Print(result);
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.Equal(5, pause.Location.Line);
        Assert.Contains(RuleScriptRuntimeEventKind.Print, asyncHandlerEvents);
        Assert.Contains(RuleScriptRuntimeEventKind.BreakpointHit, asyncHandlerEvents);
        Assert.False(session.Context?.Contains("result"));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ParallelBreakpoint_PausesRuntimeEventsFromAllTasksUntilContinue()
    {
        var observedEvents = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync(
            "Delay",
            async (args, cancellationToken) =>
            {
                await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
                return null;
            },
            threadSafe: true);
        engine.AddBreakpoint(4);
        var session = new RuleScriptDebugSession(engine);
        session.RuntimeEvent += runtimeEvent =>
        {
            if (runtimeEvent.Kind is RuleScriptRuntimeEventKind.CurrentLineChanged
                or RuleScriptRuntimeEventKind.Print)
            {
                Interlocked.Increment(ref observedEvents);
            }
        };

        var runTask = session.RunAsync("""
            done = false;
            parallel:
                task:
                    Delay(1);
                    global.done = true;
                end
                task:
                    while done == false:
                        Print("Task Alive");
                        Delay(5);
                    end
                end
            end
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.Equal(4, pause.Location.Line);
        await Task.Delay(50);
        var countWhilePaused = Volatile.Read(ref observedEvents);
        await Task.Delay(100);
        Assert.Equal(countWhilePaused, Volatile.Read(ref observedEvents));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.True(context.Get<bool>("done"));
        Assert.False(session.IsPaused);
    }

    [Fact]
    public async Task ParallelBreakpoint_StopReleasesAndCancelsAllPausedTasks()
    {
        var observedEvents = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync(
            "Delay",
            async (args, cancellationToken) =>
            {
                await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
                return null;
            },
            threadSafe: true);
        engine.AddBreakpoint(4);
        var session = new RuleScriptDebugSession(engine);
        session.RuntimeEvent += runtimeEvent =>
        {
            if (runtimeEvent.Kind is RuleScriptRuntimeEventKind.CurrentLineChanged
                or RuleScriptRuntimeEventKind.Print)
            {
                Interlocked.Increment(ref observedEvents);
            }
        };

        var runTask = session.RunAsync("""
            done = false;
            parallel:
                task:
                    Delay(1);
                    global.done = true;
                end
                task:
                    while done == false:
                        Print("Task Alive");
                        Delay(5);
                    end
                end
            end
            """);
        await session.WaitForPauseAsync(TestTimeout());

        session.Stop();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        var countAfterStop = Volatile.Read(ref observedEvents);
        await Task.Delay(100);

        Assert.Equal(countAfterStop, Volatile.Read(ref observedEvents));
        Assert.False(session.IsPaused);
        Assert.Null(session.CurrentPause);
    }

    [Fact]
    public async Task RunAsync_BreakpointAtLineFivePausesAndContinueCompletesExecution()
    {
        var printedValues = new List<object?>();
        var engine = new RuleScriptEngine();
        engine.RuntimeEventHandlerAsync = async (runtimeEvent, _) =>
        {
            await Task.Yield();

            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print)
            {
                printedValues.Add(runtimeEvent.Value);
            }

            return RuleScriptExecutionDirective.Continue;
        };
        engine.RegisterFunctionAsync("Delay", async (args, cancellationToken) =>
        {
            await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
            return null;
        });
        engine.AddBreakpoint(5);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            Print(5);
            Delay(1);
            Print(4);
            Delay(1);
            Print(3);
            Delay(1);
            Print(2);
            Delay(1);
            Print(1);
            Delay(1);
            Print(0);
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.Equal(5, pause.Location.Line);
        Assert.Equal([5d, 4d], printedValues);

        session.Continue();
        await runTask.WaitAsync(TestTimeout());

        Assert.Equal([5d, 4d, 3d, 2d, 1d, 0d], printedValues);
    }

    [Fact]
    public async Task RunAsync_StepOverMovesOneExecutableStatementAndWaitsForNextResume()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var a = 1;
            var b = 2;
            var c = 3;
            result = a + b + c;
            """);
        var breakpoint = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, breakpoint.Kind);
        Assert.Equal(2, breakpoint.Location.Line);
        Assert.False(session.Context?.Contains("b"));

        session.StepOver();
        var firstStep = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.StepPaused, firstStep.Kind);
        Assert.Equal(3, firstStep.Location.Line);
        Assert.Equal(2d, session.Context?.Get<double>("b"));
        Assert.False(session.Context?.Contains("c"));

        session.StepOver();
        var secondStep = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.StepPaused, secondStep.Kind);
        Assert.Equal(4, secondStep.Location.Line);
        Assert.Equal(3d, session.Context?.Get<double>("c"));
        Assert.False(session.Context?.Contains("result"));
        Assert.False(runTask.IsCompleted);

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public async Task RunAsync_ForwardsPrintEventsToAsyncRuntimeEventHandler()
    {
        var printedValues = new List<object?>();
        var engine = new RuleScriptEngine();
        engine.RuntimeEventHandlerAsync = async (runtimeEvent, _) =>
        {
            await Task.Yield();

            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print)
            {
                printedValues.Add(runtimeEvent.Value);
            }

            return RuleScriptExecutionDirective.Continue;
        };
        var session = new RuleScriptDebugSession(engine);

        var context = await session.RunAsync("""
            Print(5);
            Print(4);
            result = 9;
            """).WaitAsync(TestTimeout());

        Assert.Equal([5d, 4d], printedValues);
        Assert.Equal(9d, context.Get<double>("result"));
    }

    [Fact]
    public async Task Stop_WhilePausedAtBreakpoint_CancelsRunAndClearsPause()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var value = 1;
            result = value + 1;
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.True(session.IsPaused);

        session.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(session.IsPaused);
        Assert.Null(session.CurrentPause);
    }

    [Fact]
    public async Task Stop_WhilePausedAfterStepOver_CancelsRunAndClearsPause()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(1);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var a = 1;
            var b = 2;
            result = a + b;
            """);
        await session.WaitForPauseAsync(TestTimeout());

        session.StepOver();
        var stepPause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.StepPaused, stepPause.Kind);
        Assert.Equal(2, stepPause.Location.Line);
        Assert.True(session.IsPaused);

        session.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(session.IsPaused);
        Assert.Null(session.CurrentPause);
    }

    [Fact]
    public async Task Stop_WhileRunningAsyncHostFunction_CancelsRun()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("Delay", async (args, cancellationToken) =>
        {
            await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
            return null;
        });
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            Delay(30000);
            result = 1;
            """);

        await Task.Delay(50);
        session.Stop();

        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(session.Context?.Contains("result"));
    }

    [Fact]
    public async Task EngineStop_WhilePausedAtBreakpoint_CancelsDebugRunAndClearsPause()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var value = 1;
            result = value + 1;
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.True(session.IsPaused);

        engine.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(session.IsPaused);
        Assert.Null(session.CurrentPause);
        Assert.False(session.Context?.Contains("result"));
    }

    [Fact]
    public async Task Stop_RestoresRuntimeHandlers()
    {
        var syncEvents = new List<RuleScriptRuntimeEventKind>();
        var asyncEvents = new List<RuleScriptRuntimeEventKind>();
        var engine = new RuleScriptEngine();
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            syncEvents.Add(runtimeEvent.Kind);
            return RuleScriptExecutionDirective.Continue;
        };
        engine.RuntimeEventHandlerAsync = async (runtimeEvent, _) =>
        {
            await Task.Yield();
            asyncEvents.Add(runtimeEvent.Kind);
            return RuleScriptExecutionDirective.Continue;
        };
        engine.AddBreakpoint(1);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            result = 1;
            """);
        await session.WaitForPauseAsync(TestTimeout());

        session.Stop();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));

        await engine.ExecuteAsync("Print(1);").WaitAsync(TestTimeout());

        Assert.Contains(RuleScriptRuntimeEventKind.Print, asyncEvents);
        Assert.DoesNotContain(RuleScriptRuntimeEventKind.Print, syncEvents);
    }

    [Fact]
    public async Task Stop_IsIdempotentAndCancelsPendingPauseWait()
    {
        var engine = new RuleScriptEngine();
        var session = new RuleScriptDebugSession(engine);
        var waitTask = session.WaitForPauseAsync();

        session.Stop();
        session.Stop();

        await Assert.ThrowsAsync<TaskCanceledException>(() => waitTask.WaitAsync(TestTimeout()));
        Assert.False(session.IsPaused);
        Assert.Null(session.CurrentPause);
    }

    [Fact]
    public async Task StepOver_AfterBreakpointPausesAtNextExecutableStatement()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """
            var value = 1;
            value = 2;
            result = value;
            """);
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };
        engine.AddBreakpoint("main.rules", 2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunFileAsync("main.rules");
        var breakpoint = await session.WaitForPauseAsync(TestTimeout());
        Assert.Equal(2, breakpoint.Location.Line);

        session.StepOver();
        var stepPause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.StepPaused, stepPause.Kind);
        Assert.Equal(3, stepPause.Location.Line);
        Assert.False(session.Context?.Contains("result"));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task BreakpointInImportedFunction_PausesWithImportedFileLocation()
    {
        using var project = new RuleScriptProject();
        project.Write("module.rules", """
            export function Value():
                var value = 41;
                return value + 1;
            endfunction
            """);
        project.Write("main.rules", """
            import "module.rules" as module;
            result = module.Value();
            """);
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };
        engine.AddBreakpoint("module.rules", 2);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunFileAsync("main.rules");
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.BreakpointHit, pause.Kind);
        Assert.Equal(project.PathFor("module.rules"), pause.Location.File);
        Assert.Equal(2, pause.Location.Line);
        Assert.False(session.Context?.Contains("result"));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public async Task StepExecution_PausesAtFirstExecutableStatementForPlaygroundStepping()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """
            var value = 1;
            result = value + 1;
            """);
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath,
            StepExecution = true
        };
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunFileAsync("main.rules");
        var firstPause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(RuleScriptRuntimeEventKind.StepPaused, firstPause.Kind);
        Assert.Equal(1, firstPause.Location.Line);

        session.StepOver();
        var secondPause = await session.WaitForPauseAsync(TestTimeout());
        Assert.Equal(2, secondPause.Location.Line);

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(2d, context.Get<double>("result"));
    }

    private static CancellationToken TestTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
    }

    private sealed class RuleScriptProject : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"rulescript-{Guid.NewGuid():N}");

        public RuleScriptProject()
        {
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Write(string fileName, string content)
        {
            var path = PathFor(fileName);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(DirectoryPath, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
