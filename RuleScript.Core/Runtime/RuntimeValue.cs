namespace RuleScript.Core.Runtime;

public sealed record RuntimeValue(object? Value)
{
    public static readonly RuntimeValue Null = new((object?)null);

    public static RuntimeValue FromObject(object? value)
    {
        return value is RuntimeValue runtimeValue ? runtimeValue : new RuntimeValue(value);
    }

    public T? As<T>() => Value is null ? default : (T)Value;
}
