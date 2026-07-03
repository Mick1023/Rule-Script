using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160AttributedAsyncHostFunctionTests
{
    [Fact]
    public async Task RegisterFunctions_TaskOfT_RegistersAndExecutesAsyncFunction()
    {
        var engine = new RuleScriptEngine();

        var count = engine.RegisterFunctions(new AsyncHost());
        var context = await engine.ExecuteAsync("result = EngineDelay(7);");
        var function = engine.RegisteredHostFunctions.Single(symbol => symbol.Name == "EngineDelay");

        Assert.Equal(2, count);
        Assert.Equal("value-7", context.Get<string>("result"));
        Assert.True(function.IsAsync);
        Assert.Equal(RuleScriptValueType.String, function.ReturnType);
        Assert.Equal(["milliseconds"], function.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("Waits and returns a value.", function.Documentation);
    }

    [Fact]
    public async Task RegisterFunctions_TaskWithoutResult_ReturnsNull()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctions(new AsyncHost());

        var context = await engine.ExecuteAsync("result = NotifyAsync();");
        var function = engine.RegisteredHostFunctions.Single(symbol => symbol.Name == "NotifyAsync");

        Assert.Null(context.Get("result"));
        Assert.Equal(RuleScriptValueType.Null, function.ReturnType);
    }

    [Fact]
    public async Task RegisterFunctions_FinalCancellationToken_IsInjectedByEngine()
    {
        var host = new CancellableHost();
        var engine = new RuleScriptEngine();
        engine.RegisterFunctions(host);
        using var cancellation = new CancellationTokenSource();

        var execution = engine.ExecuteAsync("result = WaitForCancellation();", cancellationToken: cancellation.Token);
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await host.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(host.ObservedCancellation);
        Assert.Empty(Assert.Single(engine.RegisteredHostFunctions).Parameters);
    }

    [Fact]
    public void RegisterFunctions_CancellationTokenMustBeLast()
    {
        var engine = new RuleScriptEngine();

        var exception = Assert.Throws<NotSupportedException>(() => engine.RegisterFunctions(new InvalidHost()));

        Assert.Contains("only as its final parameter", exception.Message);
    }

    private sealed class AsyncHost
    {
        [RuleScriptFunction(
            Name = "EngineDelay",
            Documentation = "Waits and returns a value.")]
        public async Task<string> EngineDelay(double milliseconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds));
            return $"value-{milliseconds}";
        }

        [RuleScriptFunction]
        public async Task NotifyAsync()
        {
            await Task.Yield();
        }
    }

    private sealed class CancellableHost
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedCancellation { get; private set; }

        [RuleScriptFunction]
        public async Task WaitForCancellation(CancellationToken cancellationToken)
        {
            Started.SetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                CancellationObserved.SetResult();
                throw;
            }
        }
    }

    private sealed class InvalidHost
    {
        [RuleScriptFunction]
        public Task Invalid(CancellationToken cancellationToken, int value) => Task.CompletedTask;
    }
}
