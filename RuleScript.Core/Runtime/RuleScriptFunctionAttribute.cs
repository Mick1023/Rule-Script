namespace RuleScript.Core.Runtime;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RuleScriptFunctionAttribute : Attribute
{
    public string? Name { get; set; }

    public bool ThreadSafe { get; set; }

    public string? Documentation { get; set; }
}
