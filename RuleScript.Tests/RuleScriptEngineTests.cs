using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuleScriptEngineTests
{
    [Fact]
    public void Execute_WithEmptyScript_DoesNotModifyContext()
    {
        var engine = new RuleScriptEngine();
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        engine.Execute(string.Empty, context);

        Assert.Equal("Mick", context.Get<string>("Name"));
    }

    [Fact]
    public void Stop_WhenNoExecutionIsActive_DoesNotThrow()
    {
        var engine = new RuleScriptEngine();

        engine.Stop();
        engine.Stop();
    }

    [Fact]
    public async Task Stop_WhileExecuteAsyncHostFunctionIsRunning_CancelsExecution()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("Delay", async (args, cancellationToken) =>
        {
            await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
            return null;
        });
        var context = new RuntimeContext();

        var runTask = engine.ExecuteAsync("""
            Delay(30000);
            result = 1;
            """, context);

        await Task.Delay(50);
        engine.Stop();

        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(context.Contains("result"));
    }

    [Fact]
    public async Task Stop_WhileExecuteSyncLoopIsRunning_CancelsExecution()
    {
        var engine = new RuleScriptEngine
        {
            LoopIterationLimitEnabled = false
        };
        var context = new RuntimeContext();

        var runTask = Task.Run(() => engine.Execute("""
            var i = 0;
            while true:
                i = i + 1;
            endwhile
            result = i;
            """, context));

        await Task.Delay(50);
        engine.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TestTimeout()));
        Assert.False(context.Contains("result"));
    }

    private static CancellationToken TestTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
    }
}
