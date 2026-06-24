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
        Register("StartsWith", StartsWith);
        Register("EndsWith", EndsWith);
        Register("Contains", Contains);
        Register("Split", Split);
        Register("Join", Join);
        Register("Replace", Replace);
        Register("Substring", Substring);
        Register("Length", Length);
        Register("ArrayAdd", ArrayAdd);
        Register("ArrayRemove", ArrayRemove);
        Register("ArrayContains", ArrayContains);
        Register("ArrayClear", ArrayClear);
        Register("Abs", Abs);
        Register("Min", Min);
        Register("Max", Max);
        Register("Clamp", Clamp);
        Register("Round", Round);
        Register("Floor", Floor);
        Register("Ceiling", Ceiling);
        Register("ParseBool", ParseBool);
        Register("IsNull", IsNull);
        Register("TypeOf", TypeOf);
        Register("Coalesce", Coalesce);
        Register("JsonParse", JsonFunctions.JsonParse);
        Register("JsonStringify", JsonFunctions.JsonStringify);
        Register("JsonGet", JsonFunctions.JsonGet);
        Register("JsonSet", JsonFunctions.JsonSet);
        Register("JsonExists", JsonFunctions.JsonExists);
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

    private static RuntimeValue StartsWith(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("StartsWith", arguments, 2);
        return new RuntimeValue(ConvertToString(arguments[0].Value).StartsWith(ConvertToString(arguments[1].Value), StringComparison.Ordinal));
    }

    private static RuntimeValue EndsWith(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("EndsWith", arguments, 2);
        return new RuntimeValue(ConvertToString(arguments[0].Value).EndsWith(ConvertToString(arguments[1].Value), StringComparison.Ordinal));
    }

    private static RuntimeValue Contains(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Contains", arguments, 2);
        return new RuntimeValue(ConvertToString(arguments[0].Value).Contains(ConvertToString(arguments[1].Value), StringComparison.Ordinal));
    }

    private static RuntimeValue Split(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Split", arguments, 2);
        var text = ConvertToString(arguments[0].Value);
        var separator = ConvertToString(arguments[1].Value);
        return new RuntimeValue(text.Split(separator, StringSplitOptions.None).Cast<object?>().ToList());
    }

    private static RuntimeValue Join(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Join", arguments, 2);
        var separator = ConvertToString(arguments[0].Value);
        var values = RequireList("Join", arguments[1].Value);
        return new RuntimeValue(string.Join(separator, values.Cast<object?>().Select(ConvertToString)));
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

    private static RuntimeValue ArrayAdd(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayAdd", arguments, 2);
        RequireList("ArrayAdd", arguments[0].Value).Add(arguments[1].Value);
        return arguments[0];
    }

    private static RuntimeValue ArrayRemove(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayRemove", arguments, 2);
        var list = RequireList("ArrayRemove", arguments[0].Value);

        for (var i = 0; i < list.Count; i++)
        {
            if (AreValuesEqual(list[i], arguments[1].Value))
            {
                list.RemoveAt(i);
                return new RuntimeValue(true);
            }
        }

        return new RuntimeValue(false);
    }

    private static RuntimeValue ArrayContains(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayContains", arguments, 2);
        var list = RequireList("ArrayContains", arguments[0].Value);
        return new RuntimeValue(list.Cast<object?>().Any(value => AreValuesEqual(value, arguments[1].Value)));
    }

    private static RuntimeValue ArrayClear(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayClear", arguments, 1);
        RequireList("ArrayClear", arguments[0].Value).Clear();
        return arguments[0];
    }

    private static RuntimeValue Abs(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Abs", arguments, 1);
        return new RuntimeValue(Math.Abs(RequireNumber("Abs", arguments[0].Value)));
    }

    private static RuntimeValue Min(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Min", arguments, 2);
        return new RuntimeValue(Math.Min(RequireNumber("Min", arguments[0].Value), RequireNumber("Min", arguments[1].Value)));
    }

    private static RuntimeValue Max(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Max", arguments, 2);
        return new RuntimeValue(Math.Max(RequireNumber("Max", arguments[0].Value), RequireNumber("Max", arguments[1].Value)));
    }

    private static RuntimeValue Clamp(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Clamp", arguments, 3);
        var value = RequireNumber("Clamp", arguments[0].Value);
        var min = RequireNumber("Clamp", arguments[1].Value);
        var max = RequireNumber("Clamp", arguments[2].Value);
        return new RuntimeValue(Math.Clamp(value, min, max));
    }

    private static RuntimeValue Round(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Round", arguments, 1);
        return new RuntimeValue(Math.Round(RequireNumber("Round", arguments[0].Value)));
    }

    private static RuntimeValue Floor(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Floor", arguments, 1);
        return new RuntimeValue(Math.Floor(RequireNumber("Floor", arguments[0].Value)));
    }

    private static RuntimeValue Ceiling(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Ceiling", arguments, 1);
        return new RuntimeValue(Math.Ceiling(RequireNumber("Ceiling", arguments[0].Value)));
    }

    private static RuntimeValue ParseBool(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ParseBool", arguments, 1);

        if (arguments[0].Value is bool boolValue)
        {
            return new RuntimeValue(boolValue);
        }

        if (arguments[0].Value is string text && bool.TryParse(text, out var parsed))
        {
            return new RuntimeValue(parsed);
        }

        throw new RuntimeException("ParseBool expects a bool or string value.");
    }

    private static RuntimeValue IsNull(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("IsNull", arguments, 1);
        return new RuntimeValue(arguments[0].Value is null);
    }

    private static RuntimeValue TypeOf(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("TypeOf", arguments, 1);

        return new RuntimeValue(arguments[0].Value switch
        {
            null => "null",
            string => "string",
            bool => "bool",
            byte or short or int or long or float or double or decimal => "number",
            IList => "array",
            IDictionary<string, object?> => "object",
            IReadOnlyDictionary<string, object?> => "object",
            _ => "object"
        });
    }

    private static RuntimeValue Coalesce(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Coalesce", arguments, 2);
        return arguments[0].Value is null ? arguments[1] : arguments[0];
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

    private static IList RequireList(string functionName, object? value)
    {
        return value as IList ?? throw new RuntimeException($"{functionName} expects an array value.");
    }

    private static double RequireNumber(string functionName, object? value)
    {
        if (TryGetDouble(value, out var number))
        {
            return number;
        }

        throw new RuntimeException($"{functionName} expects a number value.");
    }

    private static bool TryGetDouble(object? value, out double number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case double doubleValue:
                number = doubleValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            default:
                number = 0;
                return false;
        }
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

    private static bool AreValuesEqual(object? left, object? right)
    {
        if (TryGetDouble(left, out var leftNumber) && TryGetDouble(right, out var rightNumber))
        {
            return leftNumber.Equals(rightNumber);
        }

        return Equals(left, right);
    }

    private static bool IsWholeNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Truncate(value);
    }
}
