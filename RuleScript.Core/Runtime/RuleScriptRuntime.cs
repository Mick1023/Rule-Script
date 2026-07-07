using System.Threading.Channels;

namespace RuleScript.Core.Runtime;

public enum RuleScriptRuntimeState
{
    Created,
    Running,
    Stopping,
    Stopped,
    Faulted
}

public sealed record RuleScriptTriggerReceipt(long SequenceId, string Name);

public sealed class RuleScriptRuntime
{
    private readonly Func<CancellationToken, Interpreter> _interpreterFactory;
    private readonly CancellationTokenSource _stopCancellation = new();
    private readonly object _sync = new();
    private Task? _executionTask;
    private long _nextSequenceId;

    internal RuleScriptRuntime(
        ScriptModule module,
        RuntimeContext context,
        Func<CancellationToken, Interpreter> interpreterFactory,
        RuleScriptHostTriggerDispatcher dispatcher)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        _interpreterFactory = interpreterFactory ?? throw new ArgumentNullException(nameof(interpreterFactory));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        HostTriggers = Module.HostTriggers
            .SelectMany(trigger => trigger.Value.Select(function => CreateHostTriggerSymbol(trigger.Key, function)))
            .OrderBy(function => function.HostTriggerMetadata!.Name, StringComparer.Ordinal)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .ToArray();
        HasTriggerTask = ContainsTriggerTask(Module.Statements);
    }

    internal ScriptModule Module { get; }

    internal RuleScriptHostTriggerDispatcher Dispatcher { get; }

    internal bool HasTriggerTask { get; }

    public RuntimeContext Context { get; }

    public RuleScriptRuntimeState State { get; private set; } = RuleScriptRuntimeState.Created;

    public IReadOnlyList<RuleScriptFunctionSymbol> HostTriggers { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_executionTask is not null)
            {
                return _executionTask;
            }

            State = RuleScriptRuntimeState.Running;
            _executionTask = RunAsync(cancellationToken);
            return _executionTask;
        }
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (_sync)
        {
            if (State is RuleScriptRuntimeState.Stopped or RuleScriptRuntimeState.Faulted)
            {
                return;
            }

            State = RuleScriptRuntimeState.Stopping;
            _stopCancellation.Cancel();
            Dispatcher.Complete();
            task = _executionTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask<RuleScriptTriggerReceipt> TriggerAsync(
        string name,
        IReadOnlyList<object?>? args = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Trigger name cannot be empty.", nameof(name));
        }

        if (State != RuleScriptRuntimeState.Running)
        {
            throw new InvalidOperationException("RuleScript runtime is not running.");
        }

        if (!HasTriggerTask)
        {
            throw new InvalidOperationException("RuleScript runtime has no active trigger task dispatcher.");
        }

        if (!Module.HostTriggers.ContainsKey(name))
        {
            throw new InvalidOperationException($"Host trigger '{name}' is not registered.");
        }

        var sequenceId = Interlocked.Increment(ref _nextSequenceId);
        var request = new RuleScriptHostTriggerRequest(
            sequenceId,
            name,
            (args ?? []).Select(RuntimeValue.FromObject).ToArray());
        await Dispatcher.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        return new RuleScriptTriggerReceipt(sequenceId, name);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCancellation.Token);

        try
        {
            await _interpreterFactory(linked.Token)
                .ExecuteAsync(Module, Context, linked.Token)
                .ConfigureAwait(false);
            State = RuleScriptRuntimeState.Stopped;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            State = RuleScriptRuntimeState.Stopped;
        }
        catch
        {
            State = RuleScriptRuntimeState.Faulted;
            throw;
        }
        finally
        {
            Dispatcher.Complete();
        }
    }

    private static RuleScriptFunctionSymbol CreateHostTriggerSymbol(string triggerName, UserFunction function)
    {
        var declaration = function.Declaration;
        var parameters = declaration.ParameterDefinitions.Select(parameter =>
        {
            var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                ? parsed
                : RuleScriptValueType.Unknown;
            return new RuleScriptParameterSymbol(parameter.Name, type);
        }).ToArray();

        return new RuleScriptFunctionSymbol(
            declaration.Name,
            parameters,
            RuleScriptValueType.Unknown,
            isReturnTypeNullable: false,
            declaration.IsExported,
            declaration.Documentation,
            RuleScriptFunctionKind.User,
            RuleScriptSourceMapper.CreateLocation(null, declaration.NameLine ?? declaration.Line, declaration.NameColumn ?? declaration.Column),
            RuleScriptSourceMapper.CreateRange(null, declaration.SourceSpan),
            hostTriggerMetadata: new RuleScriptHostTriggerMetadata(triggerName));
    }

    private static bool ContainsTriggerTask(IEnumerable<Parser.Ast.Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Parser.Ast.ParallelStatementSyntax parallel when parallel.Tasks.Any(task => task.Kind == Parser.Ast.TaskBlockKind.Trigger):
                    return true;
                case Parser.Ast.FunctionDeclarationStatement:
                    break;
            }
        }

        return false;
    }
}

internal sealed record RuleScriptHostTriggerRequest(
    long SequenceId,
    string Name,
    IReadOnlyList<RuntimeValue> Arguments);

internal sealed class RuleScriptHostTriggerDispatcher
{
    private readonly Channel<RuleScriptHostTriggerRequest> _queue = Channel.CreateUnbounded<RuleScriptHostTriggerRequest>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(RuleScriptHostTriggerRequest request, CancellationToken cancellationToken)
    {
        return _queue.Writer.WriteAsync(request, cancellationToken);
    }

    public ValueTask<RuleScriptHostTriggerRequest> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }

    public void Complete()
    {
        _queue.Writer.TryComplete();
    }
}
