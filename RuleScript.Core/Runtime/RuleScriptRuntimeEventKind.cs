namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes the kind of runtime event reported to a host.
/// </summary>
public enum RuleScriptRuntimeEventKind
{
    /// <summary>
    /// Execution moved to a new source line.
    /// </summary>
    CurrentLineChanged,

    /// <summary>
    /// The script called the built-in `Print` function.
    /// </summary>
    Print,

    /// <summary>
    /// Execution reached a registered breakpoint.
    /// </summary>
    BreakpointHit,

    /// <summary>
    /// Execution paused for step-over control.
    /// </summary>
    StepPaused,

    /// <summary>
    /// Script parsing or runtime execution failed.
    /// </summary>
    Error
}
