using RuleScript.Core.Diagnostics;
using System.Collections;
using System.Globalization;

namespace RuleScript.Core.Runtime;

public sealed class BuiltinFunctions
{
    private readonly Dictionary<string, Func<IReadOnlyList<RuntimeValue>, RuntimeValue>> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleScriptBuiltinFunctionSymbol> _signatures = new(StringComparer.Ordinal);

    public BuiltinFunctions()
    {
        RegisterTyped("Print", Print, RuleScriptValueType.Any, "Returns the supplied value and lets hosts observe it as a normal expression result.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ToString", ToStringValue, RuleScriptValueType.String, "Converts a value to its invariant string representation.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ParseInt", ParseInt, RuleScriptValueType.Number, "Parses a string or whole numeric value as an integer number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ParseDecimal", ParseDecimal, RuleScriptValueType.Number, "Parses a string or numeric value as a decimal number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("Trim", Trim, RuleScriptValueType.String, "Removes leading and trailing whitespace from a value converted to text.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ToUpper", ToUpper, RuleScriptValueType.String, "Converts a value to uppercase invariant text.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ToLower", ToLower, RuleScriptValueType.String, "Converts a value to lowercase invariant text.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("StartsWith", StartsWith, RuleScriptValueType.Boolean, "Returns whether text starts with the specified prefix using ordinal comparison.",
            new("value", RuleScriptValueType.Any),
            new("prefix", RuleScriptValueType.Any));
        RegisterTyped("EndsWith", EndsWith, RuleScriptValueType.Boolean, "Returns whether text ends with the specified suffix using ordinal comparison.",
            new("value", RuleScriptValueType.Any),
            new("suffix", RuleScriptValueType.Any));
        RegisterTyped("Contains", Contains, RuleScriptValueType.Boolean, "Returns whether text contains the specified search value using ordinal comparison.",
            new("value", RuleScriptValueType.Any),
            new("searchValue", RuleScriptValueType.Any));
        RegisterTyped("Split", Split, RuleScriptValueType.Array, "Splits text by the specified separator and returns an array of parts.",
            new("value", RuleScriptValueType.Any),
            new("separator", RuleScriptValueType.Any));
        RegisterTyped("Join", Join, RuleScriptValueType.String, "Joins array values into text with the specified separator.",
            new("separator", RuleScriptValueType.Any),
            new("values", RuleScriptValueType.Array));
        RegisterTyped("Replace", Replace, RuleScriptValueType.String, "Replaces all ordinal matches of oldValue with newValue in text.",
            new("value", RuleScriptValueType.Any),
            new("oldValue", RuleScriptValueType.Any),
            new("newValue", RuleScriptValueType.Any));
        RegisterTyped("Substring", Substring, RuleScriptValueType.String, "Returns a substring from text using a zero-based start index and length.",
            new("value", RuleScriptValueType.Any),
            new("start", RuleScriptValueType.Number),
            new("length", RuleScriptValueType.Number));
        RegisterTyped("Length", Length, RuleScriptValueType.Number, "Returns the number of items in an array or characters in text.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayAdd", ArrayAdd, RuleScriptValueType.Array, "Appends a value to an array and returns the same array.",
            new("array", RuleScriptValueType.Array),
            new("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayInsert", ArrayInsert, RuleScriptValueType.Array, "Inserts a value into an array at the specified zero-based index.",
            new("array", RuleScriptValueType.Array),
            new("index", RuleScriptValueType.Number),
            new("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayRemove", ArrayRemove, RuleScriptValueType.Boolean, "Removes the first matching value from an array and returns whether one was removed.",
            new("array", RuleScriptValueType.Array),
            new("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayRemoveAt", ArrayRemoveAt, RuleScriptValueType.Any, "Removes and returns the array item at the specified zero-based index.",
            new("array", RuleScriptValueType.Array),
            new("index", RuleScriptValueType.Number));
        RegisterTyped("ArraySort", ArraySort, RuleScriptValueType.Array, "Sorts an array of all strings or all numbers in ascending order.",
            new RuleScriptParameterSymbol("array", RuleScriptValueType.Array));
        RegisterTyped("ArrayContains", ArrayContains, RuleScriptValueType.Boolean, "Returns whether an array contains a matching value.",
            new("array", RuleScriptValueType.Array),
            new("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayClear", ArrayClear, RuleScriptValueType.Array, "Removes all items from an array and returns the same array.",
            new RuleScriptParameterSymbol("array", RuleScriptValueType.Array));
        RegisterTyped("ObjectKeys", ObjectKeys, RuleScriptValueType.Array, "Returns the object's keys sorted by ordinal name.",
            new RuleScriptParameterSymbol("object", RuleScriptValueType.Object));
        RegisterTyped("ObjectContainsKey", ObjectContainsKey, RuleScriptValueType.Boolean, "Returns whether an object contains the specified string key.",
            new("object", RuleScriptValueType.Object),
            new("key", RuleScriptValueType.String));
        RegisterTyped("Abs", Abs, RuleScriptValueType.Number, "Returns the absolute value of a number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Number));
        RegisterTyped("Min", Min, RuleScriptValueType.Number, "Returns the smaller of two numbers.",
            new("left", RuleScriptValueType.Number),
            new("right", RuleScriptValueType.Number));
        RegisterTyped("Max", Max, RuleScriptValueType.Number, "Returns the larger of two numbers.",
            new("left", RuleScriptValueType.Number),
            new("right", RuleScriptValueType.Number));
        RegisterTyped("Clamp", Clamp, RuleScriptValueType.Number, "Restricts a number to the inclusive min and max range.",
            new("value", RuleScriptValueType.Number),
            new("min", RuleScriptValueType.Number),
            new("max", RuleScriptValueType.Number));
        RegisterTyped("Round", Round, RuleScriptValueType.Number, "Rounds a number to the nearest whole number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Number));
        RegisterTyped("Floor", Floor, RuleScriptValueType.Number, "Returns the greatest whole number less than or equal to the number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Number));
        RegisterTyped("Ceiling", Ceiling, RuleScriptValueType.Number, "Returns the smallest whole number greater than or equal to the number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Number));
        RegisterTyped("ParseBool", ParseBool, RuleScriptValueType.Boolean, "Parses a bool or string value as a boolean.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("IsNull", IsNull, RuleScriptValueType.Boolean, "Returns whether the supplied value is null.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("TypeOf", TypeOf, RuleScriptValueType.String, "Returns the RuleScript type name for a value.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("Coalesce", Coalesce, RuleScriptValueType.Any, "Returns the value when it is not null; otherwise returns the fallback.",
            new("value", RuleScriptValueType.Any),
            new("fallback", RuleScriptValueType.Any));
        RegisterTyped("JsonParse", JsonFunctions.JsonParse, RuleScriptValueType.Object, "Parses JSON text into RuleScript object, array, and primitive values.",
            new RuleScriptParameterSymbol("json", RuleScriptValueType.String));
        RegisterTyped("JsonStringify", JsonFunctions.JsonStringify, RuleScriptValueType.String, "Serializes a RuleScript value to JSON text.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("JsonGet", JsonFunctions.JsonGet, RuleScriptValueType.Any, "Reads a value from a JSON-compatible object or array by path.",
            new("value", RuleScriptValueType.Any),
            new("path", RuleScriptValueType.String));
        RegisterTyped("JsonSet", JsonFunctions.JsonSet, RuleScriptValueType.Any, "Sets a value in a JSON-compatible object or array by path and returns the updated root.",
            new("value", RuleScriptValueType.Any),
            new("path", RuleScriptValueType.String),
            new("newValue", RuleScriptValueType.Any));
        RegisterTyped("JsonExists", JsonFunctions.JsonExists, RuleScriptValueType.Boolean, "Returns whether a path exists in a JSON-compatible object or array.",
            new("value", RuleScriptValueType.Any),
            new("path", RuleScriptValueType.String));
    }

    public IEnumerable<string> Names => _functions.Keys;

    internal IReadOnlyCollection<RuleScriptBuiltinFunctionSymbol> Signatures => _signatures.Values;

    public void Register(string name, Func<IReadOnlyList<RuntimeValue>, RuntimeValue> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _functions[name] = function ?? throw new ArgumentNullException(nameof(function));
        _signatures.Remove(name);
    }

    private void RegisterTyped(
        string name,
        Func<IReadOnlyList<RuntimeValue>, RuntimeValue> function,
        RuleScriptValueType returnType,
        string documentation,
        params RuleScriptParameterSymbol[] parameters)
    {
        Register(name, function);
        _signatures[name] = new RuleScriptBuiltinFunctionSymbol(name, parameters, returnType, documentation);
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

    private static RuntimeValue ArrayInsert(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayInsert", arguments, 3);
        var list = RequireList("ArrayInsert", arguments[0].Value);
        var index = RequireInt("ArrayInsert", "index", arguments[1].Value);

        if (index < 0 || index > list.Count)
        {
            throw new RuntimeException("ArrayInsert index is outside the array bounds.");
        }

        list.Insert(index, arguments[2].Value);
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

    private static RuntimeValue ArrayRemoveAt(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayRemoveAt", arguments, 2);
        var list = RequireList("ArrayRemoveAt", arguments[0].Value);
        var index = RequireInt("ArrayRemoveAt", "index", arguments[1].Value);

        if (index < 0 || index >= list.Count)
        {
            throw new RuntimeException("ArrayRemoveAt index is outside the array bounds.");
        }

        var removed = list[index];
        list.RemoveAt(index);
        return new RuntimeValue(removed);
    }

    private static RuntimeValue ArraySort(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArraySort", arguments, 1);
        var list = RequireList("ArraySort", arguments[0].Value);

        if (list.Cast<object?>().All(value => value is string))
        {
            var sorted = list.Cast<string>().Order(StringComparer.Ordinal).Cast<object?>().ToArray();
            CopyToList(sorted, list);
            return arguments[0];
        }

        if (list.Cast<object?>().All(value => TryGetDouble(value, out _)))
        {
            var sorted = list.Cast<object?>().OrderBy(value =>
            {
                TryGetDouble(value, out var number);
                return number;
            }).ToArray();
            CopyToList(sorted, list);
            return arguments[0];
        }

        throw new RuntimeException("ArraySort expects all elements to be numbers or all elements to be strings.");
    }

    private static RuntimeValue ObjectKeys(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ObjectKeys", arguments, 1);
        var keys = RequireObject("ObjectKeys", arguments[0].Value).Keys
            .Order(StringComparer.Ordinal)
            .Cast<object?>()
            .ToList();
        return new RuntimeValue(keys);
    }

    private static RuntimeValue ObjectContainsKey(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ObjectContainsKey", arguments, 2);
        var values = RequireObject("ObjectContainsKey", arguments[0].Value);
        var key = arguments[1].Value as string
            ?? throw new RuntimeException("ObjectContainsKey argument 'key' must be a string value.");
        return new RuntimeValue(values.ContainsKey(key));
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

    private static IReadOnlyDictionary<string, object?> RequireObject(string functionName, object? value)
    {
        return value switch
        {
            IReadOnlyDictionary<string, object?> readOnlyValues => readOnlyValues,
            IDictionary<string, object?> values => new Dictionary<string, object?>(values, StringComparer.Ordinal),
            _ => throw new RuntimeException($"{functionName} expects an object value.")
        };
    }

    private static void CopyToList(IReadOnlyList<object?> values, IList destination)
    {
        for (var index = 0; index < values.Count; index++)
        {
            destination[index] = values[index];
        }
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
