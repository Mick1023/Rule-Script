using System.Globalization;

namespace RuleScript.Core.Runtime;

public sealed record RuntimeValue(object? Value)
{
    public static readonly RuntimeValue Null = new((object?)null);

    public static RuntimeValue FromObject(object? value)
    {
        return value is RuntimeValue runtimeValue ? runtimeValue : new RuntimeValue(value);
    }

    /// <summary>
    /// Returns the value as <typeparamref name="T"/>. Numbers convert between numeric types, so a
    /// RuleScript <see cref="decimal"/> can be read as <see cref="int"/> or <see cref="double"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">
    /// The value is not a <typeparamref name="T"/>, or a number with a fraction is read as an integer type.
    /// </exception>
    /// <exception cref="OverflowException">The number is outside the range of <typeparamref name="T"/>.</exception>
    public T? As<T>()
    {
        if (Value is null)
        {
            return default;
        }

        if (Value is T value)
        {
            return value;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (RuleScriptNumbers.IsNumber(Value) && IsNumericType(targetType))
        {
            if (IsIntegerType(targetType) && !IsWholeNumber(Value))
            {
                throw new InvalidCastException(
                    $"Cannot read {Convert.ToString(Value, CultureInfo.InvariantCulture)} as {targetType.Name} because it has a fractional part.");
            }

            return (T)Convert.ChangeType(Value, targetType, CultureInfo.InvariantCulture);
        }

        return (T)Value;
    }

    private static bool IsNumericType(Type type)
    {
        return IsIntegerType(type) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static bool IsIntegerType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    }

    private static bool IsWholeNumber(object value)
    {
        return value switch
        {
            decimal number => number == decimal.Truncate(number),
            double number => double.IsFinite(number) && number == Math.Truncate(number),
            float number => float.IsFinite(number) && number == MathF.Truncate(number),
            _ => true
        };
    }
}
