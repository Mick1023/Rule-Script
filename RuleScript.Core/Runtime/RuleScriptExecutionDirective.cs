namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes how execution should continue after a host-observable pause point.
/// </summary>
public enum RuleScriptExecutionDirective
{
    /// <summary>
    /// Continue execution normally.
    /// </summary>
    Continue,

    /// <summary>
    /// Execute one statement and pause before the next executable statement.
    /// </summary>
    StepOver
}
