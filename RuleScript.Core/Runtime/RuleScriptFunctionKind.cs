namespace RuleScript.Core.Runtime;

/// <summary>
/// Identifies where a RuleScript function symbol comes from.
/// </summary>
public enum RuleScriptFunctionKind
{
    User,
    Host,
    Builtin,
    Imported
}
