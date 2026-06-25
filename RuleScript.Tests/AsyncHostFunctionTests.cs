using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class AsyncHostFunctionTests
{
    [Fact]
    public async Task ExecuteAsync_AwaitsAsyncHostFunction()
    {
        var engine = new RuleScriptEngine();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        engine.RegisterFunctionAsync("Delay", async (args, cancellationToken) =>
        {
            started.SetResult();
            await canComplete.Task.WaitAsync(cancellationToken);
            return Convert.ToInt32(args[0]);
        });

        var runTask = engine.ExecuteAsync("""
            var waited = Delay(25);
            result = waited + 1;
            """);

        await started.Task.WaitAsync(TestTimeout());

        Assert.False(runTask.IsCompleted);

        canComplete.SetResult();
        var context = await runTask.WaitAsync(TestTimeout());

        Assert.Equal(26d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_CanUseAsyncHostFunctionInUserFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("ReadAsync", async args =>
        {
            await Task.Delay(1);
            return Convert.ToInt32(args[0]);
        });

        var context = await engine.ExecuteAsync("""
            function ReadTwice(value):
                return ReadAsync(value) + ReadAsync(value);
            endfunction

            result = ReadTwice(3);
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_CanCallSynchronousHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ => 41);

        var context = await engine.ExecuteAsync("result = Read() + 1;");

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ExecuteFileAsync_CanUseAsyncHostFunction()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """
            var value = ReadAsync();
            result = value + 1;
            """);

        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("ReadAsync", _ => Task.FromResult<object?>(41));

        var context = await engine.ExecuteFileAsync(project.PathFor("main.rules"));

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToAsyncHostFunction()
    {
        var engine = new RuleScriptEngine();
        using var cancellation = new CancellationTokenSource();

        engine.RegisterFunctionAsync("Wait", async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return null;
        });

        var runTask = engine.ExecuteAsync("Wait();", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask.WaitAsync(TestTimeout()));
    }

    [Fact]
    public void Execute_AsyncHostFunction_ThrowsClearRuntimeException()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("Delay", async _ =>
        {
            await Task.Delay(1);
            return null;
        });

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("Delay();"));

        Assert.Contains("ExecuteAsync", exception.Message);
    }

    [Fact]
    public async Task UnregisterFunction_RemovesAsyncHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("Value", _ => Task.FromResult<object?>(1));

        Assert.True(engine.UnregisterFunction("Value"));

        await Assert.ThrowsAsync<RuntimeException>(() => engine.ExecuteAsync("result = Value();"));
    }

    private static TimeSpan TestTimeout()
    {
        return TimeSpan.FromSeconds(5);
    }

    private sealed class RuleScriptProject : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "rulescript-async-tests", Guid.NewGuid().ToString("N"));

        public RuleScriptProject()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Write(string fileName, string content)
        {
            File.WriteAllText(PathFor(fileName), content);
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(_directory, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
