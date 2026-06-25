namespace RuleScript.Core.Runtime;

/// <summary>
/// Provides host-controlled debug execution for editor and tooling integrations.
/// </summary>
public sealed class RuleScriptDebugSession
{
    private readonly RuleScriptEngine _engine;
    private readonly object _sync = new();
    private ManualResetEventSlim? _resumeSignal;
    private RuleScriptExecutionDirective _resumeDirective = RuleScriptExecutionDirective.Continue;
    private TaskCompletionSource<RuleScriptRuntimeEvent> _nextPause = CreatePauseCompletionSource();

    /// <summary>
    /// Creates a debug session for an engine.
    /// </summary>
    /// <param name="engine">The engine used to execute scripts.</param>
    public RuleScriptDebugSession(RuleScriptEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Raised for every runtime event observed by the session.
    /// </summary>
    public event Action<RuleScriptRuntimeEvent>? RuntimeEvent;

    /// <summary>
    /// Gets whether execution is currently paused by a breakpoint or step event.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Gets the most recent pause event.
    /// </summary>
    public RuleScriptRuntimeEvent? CurrentPause { get; private set; }

    /// <summary>
    /// Gets the runtime context used by the current or most recent run.
    /// </summary>
    public RuntimeContext? Context { get; private set; }

    /// <summary>
    /// Runs a script file on a background task and allows the host to continue or step when paused.
    /// </summary>
    /// <param name="path">The script file path to execute.</param>
    /// <param name="context">The runtime context to use, or <see langword="null"/> to create one.</param>
    /// <param name="cancellationToken">A token that can request cancellation.</param>
    /// <returns>The runtime context after execution completes.</returns>
    public Task<RuntimeContext> RunFileAsync(string path, RuntimeContext? context = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Script path cannot be empty.", nameof(path));
        }

        context ??= new RuntimeContext();
        Context = context;
        PrepareForRun();

        return Task.Run(async () =>
        {
            using var registration = cancellationToken.Register(Continue);
            _engine.RuntimeEventHandler = HandleRuntimeEvent;
            await _engine.ExecuteFileAsync(path, context, cancellationToken).ConfigureAwait(false);
            return context;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a script on a background task and allows the host to continue or step when paused.
    /// </summary>
    /// <param name="script">The script text to execute.</param>
    /// <param name="context">The runtime context to use, or <see langword="null"/> to create one.</param>
    /// <param name="cancellationToken">A token that can request cancellation.</param>
    /// <returns>The runtime context after execution completes.</returns>
    public Task<RuntimeContext> RunAsync(string script, RuntimeContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);

        context ??= new RuntimeContext();
        Context = context;
        PrepareForRun();

        return Task.Run(async () =>
        {
            using var registration = cancellationToken.Register(Continue);
            _engine.RuntimeEventHandler = HandleRuntimeEvent;
            await _engine.ExecuteAsync(script, context, cancellationToken).ConfigureAwait(false);
            return context;
        }, cancellationToken);
    }

    /// <summary>
    /// Waits until execution is paused by a breakpoint or step event.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the wait.</param>
    /// <returns>The runtime event that paused execution.</returns>
    public Task<RuleScriptRuntimeEvent> WaitForPauseAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (CurrentPause is not null && IsPaused)
            {
                return Task.FromResult(CurrentPause);
            }

            return cancellationToken.CanBeCanceled
                ? _nextPause.Task.WaitAsync(cancellationToken)
                : _nextPause.Task;
        }
    }

    /// <summary>
    /// Continues execution until the next breakpoint or the end of the script.
    /// </summary>
    public void Continue()
    {
        Resume(RuleScriptExecutionDirective.Continue);
    }

    /// <summary>
    /// Executes the current statement and pauses again before the next executable statement.
    /// </summary>
    public void StepOver()
    {
        Resume(RuleScriptExecutionDirective.StepOver);
    }

    private RuleScriptExecutionDirective HandleRuntimeEvent(RuleScriptRuntimeEvent runtimeEvent)
    {
        RuntimeEvent?.Invoke(runtimeEvent);

        if (runtimeEvent.Kind is not RuleScriptRuntimeEventKind.BreakpointHit and not RuleScriptRuntimeEventKind.StepPaused)
        {
            return RuleScriptExecutionDirective.Continue;
        }

        var signal = new ManualResetEventSlim(false);

        lock (_sync)
        {
            IsPaused = true;
            CurrentPause = runtimeEvent;
            _resumeSignal = signal;
            _nextPause.TrySetResult(runtimeEvent);
        }

        signal.Wait();

        lock (_sync)
        {
            return _resumeDirective;
        }
    }

    private void Resume(RuleScriptExecutionDirective directive)
    {
        ManualResetEventSlim? signal;

        lock (_sync)
        {
            _resumeDirective = directive;
            signal = _resumeSignal;

            if (signal is not null)
            {
                IsPaused = false;
                CurrentPause = null;
                _resumeSignal = null;
                _nextPause = CreatePauseCompletionSource();
            }
        }

        signal?.Set();
    }

    private void PrepareForRun()
    {
        lock (_sync)
        {
            IsPaused = false;
            CurrentPause = null;
            _resumeSignal = null;
            _resumeDirective = RuleScriptExecutionDirective.Continue;
            _nextPause = CreatePauseCompletionSource();
        }
    }

    private static TaskCompletionSource<RuleScriptRuntimeEvent> CreatePauseCompletionSource()
    {
        return new TaskCompletionSource<RuleScriptRuntimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
