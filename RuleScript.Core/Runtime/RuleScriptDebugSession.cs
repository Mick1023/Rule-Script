namespace RuleScript.Core.Runtime;

/// <summary>
/// Provides host-controlled debug execution for editor and tooling integrations.
/// </summary>
public sealed class RuleScriptDebugSession
{
    private readonly RuleScriptEngine _engine;
    private readonly object _sync = new();
    private CancellationTokenSource? _stopCancellation;
    private ManualResetEventSlim? _resumeSignal;
    private RuleScriptExecutionDirective _resumeDirective = RuleScriptExecutionDirective.Continue;
    private TaskCompletionSource<RuleScriptRuntimeEvent> _nextPause = CreatePauseCompletionSource();
    private bool _stopRequested;

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
        var stopCancellation = CreateStopCancellation();
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellation.Token);

        return Task.Run(async () =>
        {
            using var stopRegistration = cancellationToken.Register(Stop);
            using (stopCancellation)
            using (linkedCancellation)
            {
                var previousHandler = _engine.RuntimeEventHandler;
                var previousAsyncHandler = _engine.RuntimeEventHandlerAsync;

                try
                {
                    _engine.RuntimeEventHandler = runtimeEvent => HandleRuntimeEvent(runtimeEvent, previousHandler);
                    _engine.RuntimeEventHandlerAsync = (runtimeEvent, token) => HandleRuntimeEventAsync(runtimeEvent, previousHandler, previousAsyncHandler, token);
                    await _engine.ExecuteFileAsync(path, context, linkedCancellation.Token).ConfigureAwait(false);
                    return context;
                }
                finally
                {
                    _engine.RuntimeEventHandler = previousHandler;
                    _engine.RuntimeEventHandlerAsync = previousAsyncHandler;
                    ClearStopCancellation(stopCancellation);
                }
            }
        });
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
        var stopCancellation = CreateStopCancellation();
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellation.Token);

        return Task.Run(async () =>
        {
            using var stopRegistration = cancellationToken.Register(Stop);
            using (stopCancellation)
            using (linkedCancellation)
            {
                var previousHandler = _engine.RuntimeEventHandler;
                var previousAsyncHandler = _engine.RuntimeEventHandlerAsync;

                try
                {
                    _engine.RuntimeEventHandler = runtimeEvent => HandleRuntimeEvent(runtimeEvent, previousHandler);
                    _engine.RuntimeEventHandlerAsync = (runtimeEvent, token) => HandleRuntimeEventAsync(runtimeEvent, previousHandler, previousAsyncHandler, token);
                    await _engine.ExecuteAsync(script, context, linkedCancellation.Token).ConfigureAwait(false);
                    return context;
                }
                finally
                {
                    _engine.RuntimeEventHandler = previousHandler;
                    _engine.RuntimeEventHandlerAsync = previousAsyncHandler;
                    ClearStopCancellation(stopCancellation);
                }
            }
        });
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

    /// <summary>
    /// Stops the current debug run and releases any active breakpoint or step pause.
    /// </summary>
    public void Stop()
    {
        ManualResetEventSlim? signal;
        CancellationTokenSource? stopCancellation;
        TaskCompletionSource<RuleScriptRuntimeEvent>? pauseCompletion;

        lock (_sync)
        {
            _stopRequested = true;
            IsPaused = false;
            CurrentPause = null;
            signal = _resumeSignal;
            _resumeSignal = null;
            stopCancellation = _stopCancellation;
            pauseCompletion = _nextPause;
            _nextPause = CreatePauseCompletionSource();
        }

        pauseCompletion.TrySetCanceled();
        signal?.Set();

        try
        {
            stopCancellation?.Cancel();
            _engine.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private RuleScriptExecutionDirective HandleRuntimeEvent(
        RuleScriptRuntimeEvent runtimeEvent,
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler,
        CancellationToken cancellationToken = default)
    {
        RuntimeEvent?.Invoke(runtimeEvent);

        cancellationToken.ThrowIfCancellationRequested();

        if (runtimeEvent.Kind is not RuleScriptRuntimeEventKind.BreakpointHit and not RuleScriptRuntimeEventKind.StepPaused)
        {
            return previousHandler?.Invoke(runtimeEvent) ?? RuleScriptExecutionDirective.Continue;
        }

        previousHandler?.Invoke(runtimeEvent);

        var signal = new ManualResetEventSlim(false);

        lock (_sync)
        {
            IsPaused = true;
            CurrentPause = runtimeEvent;
            _resumeSignal = signal;
            _nextPause.TrySetResult(runtimeEvent);
        }

        using var cancellationRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state => ((ManualResetEventSlim)state!).Set(), signal)
            : default;

        signal.Wait();

        lock (_sync)
        {
            if (_stopRequested || cancellationToken.IsCancellationRequested)
            {
                IsPaused = false;
                CurrentPause = null;

                if (ReferenceEquals(_resumeSignal, signal))
                {
                    _resumeSignal = null;
                }

                _nextPause = CreatePauseCompletionSource();
                throw new OperationCanceledException("Debug session was stopped.", cancellationToken);
            }

            return _resumeDirective;
        }
    }

    private async Task<RuleScriptExecutionDirective> HandleRuntimeEventAsync(
        RuleScriptRuntimeEvent runtimeEvent,
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler,
        Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>>? previousAsyncHandler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (runtimeEvent.Kind is not RuleScriptRuntimeEventKind.BreakpointHit and not RuleScriptRuntimeEventKind.StepPaused)
        {
            RuntimeEvent?.Invoke(runtimeEvent);

            if (previousAsyncHandler is not null)
            {
                return await previousAsyncHandler(runtimeEvent, cancellationToken).ConfigureAwait(false);
            }

            return previousHandler?.Invoke(runtimeEvent) ?? RuleScriptExecutionDirective.Continue;
        }

        previousHandler?.Invoke(runtimeEvent);

        if (previousAsyncHandler is not null)
        {
            await previousAsyncHandler(runtimeEvent, cancellationToken).ConfigureAwait(false);
        }

        return HandleRuntimeEvent(runtimeEvent, previousHandler: null, cancellationToken);
    }

    private void Resume(RuleScriptExecutionDirective directive)
    {
        ManualResetEventSlim? signal;

        lock (_sync)
        {
            if (_stopRequested)
            {
                return;
            }

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
            _stopRequested = false;
            _nextPause = CreatePauseCompletionSource();
        }
    }

    private CancellationTokenSource CreateStopCancellation()
    {
        var stopCancellation = new CancellationTokenSource();

        lock (_sync)
        {
            _stopCancellation = stopCancellation;
        }

        return stopCancellation;
    }

    private void ClearStopCancellation(CancellationTokenSource stopCancellation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_stopCancellation, stopCancellation))
            {
                _stopCancellation = null;
            }

            IsPaused = false;
            CurrentPause = null;
            _resumeSignal = null;
        }
    }

    private static TaskCompletionSource<RuleScriptRuntimeEvent> CreatePauseCompletionSource()
    {
        return new TaskCompletionSource<RuleScriptRuntimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
