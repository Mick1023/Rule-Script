namespace RuleScript.Core.Runtime;

/// <summary>
/// Provides host-controlled debug execution for editor and tooling integrations.
/// </summary>
public sealed class RuleScriptDebugSession
{
    private readonly RuleScriptEngine _engine;
    private readonly object _sync = new();
    private CancellationTokenSource? _stopCancellation;
    private TaskCompletionSource<RuleScriptExecutionDirective>? _resumeCompletion;
    private TaskCompletionSource<RuleScriptRuntimeEvent> _nextPause = CreatePauseCompletionSource();
    private bool _stopRequested;
    private RuleScriptRuntime? _activeRuntime;

    /// <summary>
    /// Creates a debug session for an engine.
    /// </summary>
    /// <param name="engine">The engine used to execute scripts.</param>
    public RuleScriptDebugSession(RuleScriptEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Creates a long-running runtime that reports runtime events through this debug session.
    /// </summary>
    /// <param name="script">The script text to execute.</param>
    /// <param name="context">The runtime context to use, or <see langword="null"/> to create one.</param>
    /// <returns>A runtime configured with this session's debug event handling.</returns>
    public RuleScriptRuntime CreateRuntime(string script, RuntimeContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        context ??= new RuntimeContext();
        Context = context;
        PrepareForRun();
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler = null;
        Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>>? previousAsyncHandler = null;
        RuleScriptRuntime? runtime = null;

        runtime = _engine.CreateRuntime(
            script,
            context,
            onStarting: () =>
            {
                previousHandler = _engine.RuntimeEventHandler;
                previousAsyncHandler = _engine.RuntimeEventHandlerAsync;
                _engine.RuntimeEventHandler = runtimeEvent => HandleRuntimeEvent(runtimeEvent, previousHandler);
                _engine.RuntimeEventHandlerAsync = (runtimeEvent, token) => HandleRuntimeEventAsync(runtimeEvent, previousHandler, previousAsyncHandler, token);
            },
            onCompleted: () =>
            {
                _engine.RuntimeEventHandler = previousHandler;
                _engine.RuntimeEventHandlerAsync = previousAsyncHandler;
                ClearActiveRuntime(runtime);
            });
        SetActiveRuntime(runtime);
        return runtime;
    }

    /// <summary>
    /// Creates a long-running runtime from a script file using the engine working directory and import resolver.
    /// </summary>
    /// <param name="path">The script file path to execute.</param>
    /// <param name="context">The runtime context to use, or <see langword="null"/> to create one.</param>
    /// <returns>A runtime configured with this session's debug event handling.</returns>
    public RuleScriptRuntime CreateRuntimeFromFile(string path, RuntimeContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Script path cannot be empty.", nameof(path));
        }

        context ??= new RuntimeContext();
        Context = context;
        PrepareForRun();
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler = null;
        Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>>? previousAsyncHandler = null;
        RuleScriptRuntime? runtime = null;

        runtime = _engine.CreateRuntimeFromFile(
            path,
            context,
            onStarting: () =>
            {
                previousHandler = _engine.RuntimeEventHandler;
                previousAsyncHandler = _engine.RuntimeEventHandlerAsync;
                _engine.RuntimeEventHandler = runtimeEvent => HandleRuntimeEvent(runtimeEvent, previousHandler);
                _engine.RuntimeEventHandlerAsync = (runtimeEvent, token) => HandleRuntimeEventAsync(runtimeEvent, previousHandler, previousAsyncHandler, token);
            },
            onCompleted: () =>
            {
                _engine.RuntimeEventHandler = previousHandler;
                _engine.RuntimeEventHandlerAsync = previousAsyncHandler;
                ClearActiveRuntime(runtime);
            });
        SetActiveRuntime(runtime);
        return runtime;
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
    /// Gets the variables and call stack captured at the current pause point.
    /// </summary>
    public RuleScriptDebugSnapshot? CurrentSnapshot => CurrentPause?.DebugSnapshot;

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
        TaskCompletionSource<RuleScriptExecutionDirective>? resumeCompletion;
        CancellationTokenSource? stopCancellation;
        TaskCompletionSource<RuleScriptRuntimeEvent>? pauseCompletion;
        RuleScriptRuntime? activeRuntime;

        lock (_sync)
        {
            _stopRequested = true;
            IsPaused = false;
            CurrentPause = null;
            resumeCompletion = _resumeCompletion;
            _resumeCompletion = null;
            stopCancellation = _stopCancellation;
            activeRuntime = _activeRuntime;
            pauseCompletion = _nextPause;
            _nextPause = CreatePauseCompletionSource();
        }

        pauseCompletion.TrySetCanceled();
        resumeCompletion?.TrySetCanceled();

        try
        {
            _ = activeRuntime?.StopAsync();
            stopCancellation?.Cancel();
            _engine.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SetActiveRuntime(RuleScriptRuntime runtime)
    {
        lock (_sync)
        {
            _activeRuntime = runtime;
        }
    }

    private void ClearActiveRuntime(RuleScriptRuntime? runtime)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeRuntime, runtime))
            {
                _activeRuntime = null;
            }
        }
    }

    private RuleScriptExecutionDirective HandleRuntimeEvent(
        RuleScriptRuntimeEvent runtimeEvent,
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isPauseEvent = IsPauseEvent(runtimeEvent);
        var (resumeTask, ownsPause) = EnterPause(runtimeEvent, isPauseEvent);
        if (resumeTask is not null && !ownsPause)
        {
            var resumedDirective = WaitForResume(resumeTask, cancellationToken);
            if (isPauseEvent)
            {
                return resumedDirective;
            }
        }

        RuntimeEvent?.Invoke(runtimeEvent);

        if (!isPauseEvent)
        {
            return previousHandler?.Invoke(runtimeEvent) ?? RuleScriptExecutionDirective.Continue;
        }

        previousHandler?.Invoke(runtimeEvent);
        return WaitForResume(resumeTask!, cancellationToken);
    }

    private async Task<RuleScriptExecutionDirective> HandleRuntimeEventAsync(
        RuleScriptRuntimeEvent runtimeEvent,
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? previousHandler,
        Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>>? previousAsyncHandler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isPauseEvent = IsPauseEvent(runtimeEvent);
        var (resumeTask, ownsPause) = EnterPause(runtimeEvent, isPauseEvent);
        if (resumeTask is not null && !ownsPause)
        {
            var resumedDirective = await WaitForResumeAsync(resumeTask, cancellationToken).ConfigureAwait(false);
            if (isPauseEvent)
            {
                return resumedDirective;
            }
        }

        if (!isPauseEvent)
        {
            RuntimeEvent?.Invoke(runtimeEvent);

            if (previousAsyncHandler is not null)
            {
                return await previousAsyncHandler(runtimeEvent, cancellationToken).ConfigureAwait(false);
            }

            return previousHandler?.Invoke(runtimeEvent) ?? RuleScriptExecutionDirective.Continue;
        }

        RuntimeEvent?.Invoke(runtimeEvent);
        previousHandler?.Invoke(runtimeEvent);

        if (previousAsyncHandler is not null)
        {
            await previousAsyncHandler(runtimeEvent, cancellationToken).ConfigureAwait(false);
        }

        return await WaitForResumeAsync(resumeTask!, cancellationToken).ConfigureAwait(false);
    }

    private (Task<RuleScriptExecutionDirective>? ResumeTask, bool OwnsPause) EnterPause(
        RuleScriptRuntimeEvent runtimeEvent,
        bool isPauseEvent)
    {
        lock (_sync)
        {
            if (_stopRequested)
            {
                throw new OperationCanceledException("Debug session was stopped.");
            }

            if (IsPaused)
            {
                return (_resumeCompletion!.Task, false);
            }

            if (!isPauseEvent)
            {
                return (null, false);
            }

            _resumeCompletion = new TaskCompletionSource<RuleScriptExecutionDirective>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            IsPaused = true;
            CurrentPause = runtimeEvent;
            _nextPause.TrySetResult(runtimeEvent);
            return (_resumeCompletion.Task, true);
        }
    }

    private RuleScriptExecutionDirective WaitForResume(
        Task<RuleScriptExecutionDirective> resumeTask,
        CancellationToken cancellationToken)
    {
        try
        {
            var directive = resumeTask.WaitAsync(cancellationToken).GetAwaiter().GetResult();
            ThrowIfStopped(cancellationToken);
            return directive;
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Debug session was stopped.", cancellationToken);
        }
    }

    private async Task<RuleScriptExecutionDirective> WaitForResumeAsync(
        Task<RuleScriptExecutionDirective> resumeTask,
        CancellationToken cancellationToken)
    {
        try
        {
            var directive = await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfStopped(cancellationToken);
            return directive;
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Debug session was stopped.", cancellationToken);
        }
    }

    private void ThrowIfStopped(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_stopRequested)
            {
                throw new OperationCanceledException("Debug session was stopped.", cancellationToken);
            }
        }
    }

    private static bool IsPauseEvent(RuleScriptRuntimeEvent runtimeEvent)
    {
        return runtimeEvent.Kind is RuleScriptRuntimeEventKind.BreakpointHit
            or RuleScriptRuntimeEventKind.StepPaused;
    }

    private void Resume(RuleScriptExecutionDirective directive)
    {
        TaskCompletionSource<RuleScriptExecutionDirective>? resumeCompletion;

        lock (_sync)
        {
            if (_stopRequested)
            {
                return;
            }

            resumeCompletion = _resumeCompletion;

            if (resumeCompletion is not null)
            {
                IsPaused = false;
                CurrentPause = null;
                _resumeCompletion = null;
                _nextPause = CreatePauseCompletionSource();
            }
        }

        resumeCompletion?.TrySetResult(directive);
    }

    private void PrepareForRun()
    {
        lock (_sync)
        {
            IsPaused = false;
            CurrentPause = null;
            _resumeCompletion = null;
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
            _resumeCompletion = null;
        }
    }

    private static TaskCompletionSource<RuleScriptRuntimeEvent> CreatePauseCompletionSource()
    {
        return new TaskCompletionSource<RuleScriptRuntimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
