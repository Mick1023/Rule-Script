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
        Assert.Empty(asyncHandlerEvents);
        Assert.False(session.Context?.Contains("result"));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(3d, context.Get<double>("result"));
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
            function Value():
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
