using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuntimeNotificationTests
{
    [Fact]
    public void Print_NotifiesHostWithValueAndLocation()
    {
        var engine = new RuleScriptEngine();
        var events = new List<RuleScriptRuntimeEvent>();
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            events.Add(runtimeEvent);
            return RuleScriptExecutionDirective.Continue;
        };

        engine.Execute("""
            var value = "hello";
            Print(value);
            """);

        var printEvent = Assert.Single(events.Where(runtimeEvent => runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print));
        Assert.Equal("hello", printEvent.Value);
        Assert.Equal("<script>", printEvent.Location.File);
        Assert.Equal(2, printEvent.Location.Line);
    }

    [Fact]
    public void CurrentLineChanged_NotifiesHostForExecutableStatements()
    {
        var engine = new RuleScriptEngine();
        var currentLines = new List<int?>();
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.CurrentLineChanged)
            {
                currentLines.Add(runtimeEvent.Location.Line);
            }

            return RuleScriptExecutionDirective.Continue;
        };

        var context = engine.Execute("""
            var a = 1;
            var b = 2;
            result = a + b;
            """);

        Assert.Equal(3d, context.Get<double>("result"));
        Assert.Contains(1, currentLines);
        Assert.Contains(2, currentLines);
        Assert.Contains(3, currentLines);
        Assert.Equal(3, context.CurrentLocation?.Line);
    }

    [Fact]
    public void Breakpoint_NotifiesHostBeforeStatementExecutes()
    {
        var engine = new RuleScriptEngine();
        var hitValues = new List<object?>();
        engine.AddBreakpoint(2);
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.BreakpointHit)
            {
                hitValues.Add(runtimeEvent.Location.Line);
            }

            return RuleScriptExecutionDirective.Continue;
        };

        var context = engine.Execute("""
            var value = 1;
            value = 2;
            result = value;
            """);

        Assert.Equal(2d, context.Get<double>("result"));
        Assert.Equal([2], hitValues);
    }

    [Fact]
    public void StepExecution_NotifiesHostForEachExecutableStatement()
    {
        var engine = new RuleScriptEngine
        {
            StepExecution = true
        };
        var stepLines = new List<int?>();
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.StepPaused)
            {
                stepLines.Add(runtimeEvent.Location.Line);
                return RuleScriptExecutionDirective.StepOver;
            }

            return RuleScriptExecutionDirective.Continue;
        };

        engine.Execute("""
            var a = 1;
            var b = 2;
            result = a + b;
            """);

        Assert.Equal([1, 2, 3], stepLines);
    }

    [Fact]
    public void SyntaxError_NotifiesHostWithFileAndLine()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "var value = ;");
        var engine = new RuleScriptEngine();
        RuleScriptRuntimeEvent? errorEvent = null;
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Error)
            {
                errorEvent = runtimeEvent;
            }

            return RuleScriptExecutionDirective.Continue;
        };

        var exception = Assert.Throws<SyntaxException>(() => engine.ExecuteFile(project.PathFor("main.rules")));

        Assert.Same(exception, errorEvent?.Exception);
        Assert.Equal(project.PathFor("main.rules"), errorEvent?.Location.File);
        Assert.Equal(1, errorEvent?.Location.Line);
    }

    [Fact]
    public void RuntimeErrorInImportedFunction_NotifiesHostWithImportedFileAndLine()
    {
        using var project = new RuleScriptProject();
        project.Write("module.rules", """
            function Value():
                return missing;
            endfunction
            """);
        project.Write("main.rules", """
            import "module.rules" as module;
            result = module.Value();
            """);
        var engine = new RuleScriptEngine();
        RuleScriptRuntimeEvent? errorEvent = null;
        engine.RuntimeEventHandler = runtimeEvent =>
        {
            if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Error)
            {
                errorEvent = runtimeEvent;
            }

            return RuleScriptExecutionDirective.Continue;
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.ExecuteFile(project.PathFor("main.rules")));

        Assert.Same(exception, errorEvent?.Exception);
        Assert.Equal(project.PathFor("module.rules"), errorEvent?.Location.File);
        Assert.Equal(2, errorEvent?.Location.Line);
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
