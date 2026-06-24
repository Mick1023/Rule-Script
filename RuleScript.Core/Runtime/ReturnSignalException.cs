namespace RuleScript.Core.Runtime;

internal sealed class ReturnSignalException(RuntimeValue value) : Exception
{
    public RuntimeValue Value { get; } = value;
}
