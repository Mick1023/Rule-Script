using System.Collections.ObjectModel;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Captures variables and the call stack at a debugger pause point.
/// </summary>
public sealed class RuleScriptDebugSnapshot
{
    internal RuleScriptDebugSnapshot(
        RuleScriptSourceLocation location,
        IReadOnlyDictionary<string, RuntimeValue> globals,
        IReadOnlyDictionary<string, RuntimeValue> locals,
        IEnumerable<string> callStack)
    {
        Location = location;
        Globals = Copy(globals);
        Locals = Copy(locals);
        CallStack = callStack.ToArray();
    }

    public RuleScriptSourceLocation Location { get; }

    public IReadOnlyDictionary<string, RuntimeValue> Globals { get; }

    public IReadOnlyDictionary<string, RuntimeValue> Locals { get; }

    public IReadOnlyList<string> CallStack { get; }

    private static IReadOnlyDictionary<string, RuntimeValue> Copy(IReadOnlyDictionary<string, RuntimeValue> values)
    {
        return new ReadOnlyDictionary<string, RuntimeValue>(
            new Dictionary<string, RuntimeValue>(values, StringComparer.Ordinal));
    }
}
