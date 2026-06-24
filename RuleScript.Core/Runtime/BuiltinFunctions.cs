using RuleScript.Core.Diagnostics;
using System.Collections;
using System.Globalization;

namespace RuleScript.Core.Runtime;

public sealed class BuiltinFunctions
{
    private readonly Dictionary<string, Func<IReadOnlyList<RuntimeValue>, RuntimeValue>> _functions = new(StringComparer.Ordinal);

    public BuiltinFunctions()
    {
        Register("Print", Print);
        Register("ToString", ToStringValue);
        Register("ParseInt", ParseInt);
        Register("ParseDecimal", ParseDecimal);
        Register("Trim", Trim);
        Register("ToUpper", ToUpper);
        Register("ToLower", ToLower);
        Register("Replace", Replace);
        Register("Substring", Substring);
        Register("Length", Length);
        Register("JsonParse", JsonFunctions.JsonParse);
        Register("JsonStringify", JsonFunctions.JsonStringify);
        Register("JsonGet", JsonFunctions.JsonGet);
        Register("JsonSet", JsonFunctions.JsonSet);
    }

    public IEnumerable<string> Names => _functions.Keys;

    public void Register(string name, Func<IReadOnlyList<RuntimeValue>, RuntimeValue> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _functions[name] = function ?? throw new ArgumentNullException(nameof(function));
    }

    public RuntimeValue Invoke(string name, IReadOnlyList<RuntimeValue> arguments)
    {
        if (!_functions.TryGetValue(name, out var function))
        {
            throw new RuntimeException($"Builtin function '{name}' is not registered.");
        }

        return function(arguments);
    }

    public bool TryInvoke(string name, IReadOnlyList<RuntimeValue> arguments, out RuntimeValue value)
    {
        if (_functions.TryGetValue(name, out var function))
        {
            value = function(arguments);
            return true;
        }

        value = RuntimeValue.Null;
        return false;
    }

    private static RuntimeValue Print(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Print", arguments, 1);
        return arguments[0];
    }

    private static RuntimeValue ToStringValue(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ToString", arguments, 1);
        return new RuntimeValue(ConvertToString(arguments[0].Value));
    }

    private static RuntimeValue ParseInt(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ParseInt", arguments, 1);
        var value = arguments[0].Value;

        if (TryGetInteger(value, out var intValue))
        {
            return new RuntimeValue(intValue);
        }

        if (value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
        {
            return new RuntimeValue(intValue);
        }

        throw new RuntimeException("ParseInt expects a string or whole number value.");
    }

    private static RuntimeValue ParseDecimal(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ParseDecimal", arguments, 1);
        var value = arguments[0].Value;

        if (TryGetDecimal(value, out var decimalValue))
        {
            return new RuntimeValue(decimalValue);
        }

        if (value is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue))
        {
            return new RuntimeValue(decimalValue);
        }

        throw new RuntimeException("ParseDecimal expects a string or number value.");
    }

    private static RuntimeValue Trim(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Trim", arguments, 1);
        return new RuntimeValue(ConvertToString(arguments[0].Value).Trim());
    }

    private static RuntimeValue ToUpper(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ToUpper", arguments, 1);
        return new RuntimeValue(ConvertToString(arguments[0].Value).ToUpperInvariant());
    }

    private static RuntimeValue ToLower(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ToLower", arguments, 1);
        return new RuntimeValue(ConvertToString(arguments[0].Value).ToLowerInvariant());
    }

    private static RuntimeValue Replace(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Replace", arguments, 3);
        var value = ConvertToString(arguments[0].Value);
        var oldValue = ConvertToString(arguments[1].Value);
        var newValue = ConvertToString(arguments[2].Value);

        return new RuntimeValue(value.Replace(oldValue, newValue, StringComparison.Ordinal));
    }

    private static RuntimeValue Substring(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Substring", arguments, 3);
        var value = ConvertToString(arguments[0].Value);
        var start = RequireInt("Substring", "start", arguments[1].Value);
        var length = RequireInt("Substring", "length", arguments[2].Value);

        if (start < 0 || length < 0 || start > value.Length || start + length > value.Length)
        {
            throw new RuntimeException("Substring range is outside the input string.");
        }

        return new RuntimeValue(value.Substring(start, length));
    }

    private static RuntimeValue Length(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Length", arguments, 1);
        if (arguments[0].Value is IList list)
        {
            return new RuntimeValue(list.Count);
        }

        return new RuntimeValue(ConvertToString(arguments[0].Value).Length);
    }

    private static void EnsureArgumentCount(string name, IReadOnlyList<RuntimeValue> arguments, int expected)
    {
        if (arguments.Count != expected)
        {
            throw new RuntimeException($"Builtin function '{name}' expects {expected} argument(s), but received {arguments.Count}.");
        }
    }

    private static int RequireInt(string functionName, string argumentName, object? value)
    {
        if (TryGetInteger(value, out var intValue))
        {
            return intValue;
        }

        throw new RuntimeException($"{functionName} argument '{argumentName}' must be an int value.");
    }

    private static bool TryGetInteger(object? value, out int intValue)
    {
        switch (value)
        {
            case byte byteValue:
                intValue = byteValue;
                return true;
            case short shortValue:
                intValue = shortValue;
                return true;
            case int directValue:
                intValue = directValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)longValue;
                return true;
            case float floatValue when IsWholeNumber(floatValue) && floatValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)floatValue;
                return true;
            case double doubleValue when IsWholeNumber(doubleValue) && doubleValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)doubleValue;
                return true;
            case decimal decimalValue when decimalValue == decimal.Truncate(decimalValue) && decimalValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)decimalValue;
                return true;
            default:
                intValue = 0;
                return false;
        }
    }

    private static bool TryGetDecimal(object? value, out decimal decimalValue)
    {
        switch (value)
        {
            case byte byteValue:
                decimalValue = byteValue;
                return true;
            case short shortValue:
                decimalValue = shortValue;
                return true;
            case int intValue:
                decimalValue = intValue;
                return true;
            case long longValue:
                decimalValue = longValue;
                return true;
            case float floatValue:
                decimalValue = (decimal)floatValue;
                return true;
            case double doubleValue:
                decimalValue = (decimal)doubleValue;
                return true;
            case decimal directValue:
                decimalValue = directValue;
                return true;
            default:
                decimalValue = 0;
                return false;
        }
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool IsWholeNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Truncate(value);
    }
}
