using RuleScript.Core.Diagnostics;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a host-observable runtime event.
/// </summary>
public sealed class RuleScriptRuntimeEvent
{
    /// <summary>
    /// Creates a runtime event.
    /// </summary>
    /// <param name="kind">The kind of event being reported.</param>
    /// <param name="location">The source location associated with the event.</param>
    /// <param name="message">An optional human-readable message.</param>
    /// <param name="value">An optional event value, such as a printed value.</param>
    /// <param name="exception">The exception associated with an error event.</param>
    public RuleScriptRuntimeEvent(
        RuleScriptRuntimeEventKind kind,
        RuleScriptSourceLocation location,
        string? message = null,
        object? value = null,
        RuleScriptException? exception = null)
        : this(kind, location, message, value, exception, null, null)
    {
    }

    /// <summary>
    /// Creates a runtime event with a full source range and debugger snapshot.
    /// </summary>
    public RuleScriptRuntimeEvent(
        RuleScriptRuntimeEventKind kind,
        RuleScriptSourceLocation location,
        string? message,
        object? value,
        RuleScriptException? exception,
        RuleScriptSourceRange? range,
        RuleScriptDebugSnapshot? debugSnapshot)
    {
        Kind = kind;
        Location = location;
        Message = message;
        Value = value;
        Exception = exception;
        Range = range;
        DebugSnapshot = debugSnapshot;
    }

    /// <summary>
    /// Gets the kind of event being reported.
    /// </summary>
    public RuleScriptRuntimeEventKind Kind { get; }

    /// <summary>
    /// Gets the source location associated with the event.
    /// </summary>
    public RuleScriptSourceLocation Location { get; }

    /// <summary>
    /// Gets the optional human-readable event message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the optional event value, such as a value passed to `Print`.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the exception associated with an error event.
    /// </summary>
    public RuleScriptException? Exception { get; }

    /// <summary>
    /// Gets the full source range associated with the event when available.
    /// </summary>
    public RuleScriptSourceRange? Range { get; }

    /// <summary>
    /// Gets the debugger state captured for a pause event when available.
    /// </summary>
    public RuleScriptDebugSnapshot? DebugSnapshot { get; }
}
