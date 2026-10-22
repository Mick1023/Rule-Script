using RuleScript.Core.Diagnostics;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RuleScript.Core.Runtime;

public sealed class BuiltinFunctions
{
    private readonly Dictionary<string, Func<IReadOnlyList<RuntimeValue>, RuntimeValue>> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RuleScriptFunctionSymbol>> _signatures = new(StringComparer.Ordinal);
    private RuleScriptFunctionSymbol[]? _signatureSnapshot;

    /// <summary>
    /// The largest array <c>Range</c> creates, so a typo cannot allocate an unbounded array.
    /// </summary>
    internal const int MaxRangeLength = 1_000_000;

    /// <summary>
    /// The widest string <c>PadLeft</c> and <c>PadRight</c> create.
    /// </summary>
    internal const int MaxPadWidth = 1_000_000;

    /// <summary>
    /// How long one regular expression built-in may run, so a catastrophic pattern cannot hang a script.
    /// </summary>
    internal static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

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
        RegisterTyped("IndexOf", IndexOf, RuleScriptValueType.Number, "Returns the zero-based index of the first ordinal match of searchValue in text, or -1 when there is none.",
            new("value", RuleScriptValueType.Any),
            new("searchValue", RuleScriptValueType.Any));
        RegisterTyped("LastIndexOf", LastIndexOf, RuleScriptValueType.Number, "Returns the zero-based index of the last ordinal match of searchValue in text, or -1 when there is none.",
            new("value", RuleScriptValueType.Any),
            new("searchValue", RuleScriptValueType.Any));
        RegisterTyped("PadLeft", PadLeft, RuleScriptValueType.String, "Pads text on the left with spaces to the specified total width.",
            new("value", RuleScriptValueType.Any),
            new("totalWidth", RuleScriptValueType.Any));
        RegisterTyped("PadLeft", PadLeft, RuleScriptValueType.String, "Pads text on the left with a single padding character to the specified total width.",
            new("value", RuleScriptValueType.Any),
            new("totalWidth", RuleScriptValueType.Any),
            new("padding", RuleScriptValueType.Any));
        RegisterTyped("PadRight", PadRight, RuleScriptValueType.String, "Pads text on the right with spaces to the specified total width.",
            new("value", RuleScriptValueType.Any),
            new("totalWidth", RuleScriptValueType.Any));
        RegisterTyped("PadRight", PadRight, RuleScriptValueType.String, "Pads text on the right with a single padding character to the specified total width.",
            new("value", RuleScriptValueType.Any),
            new("totalWidth", RuleScriptValueType.Any),
            new("padding", RuleScriptValueType.Any));
        RegisterTyped("FormatNumber", FormatNumber, RuleScriptValueType.String, "Formats a number with a .NET numeric format string, such as \"0.00\" or \"N0\", using invariant culture.",
            new("value", RuleScriptValueType.Any),
            new("format", RuleScriptValueType.Any));
        RegisterTyped("RegexIsMatch", RegexIsMatch, RuleScriptValueType.Boolean, "Returns whether text contains a match for a .NET regular expression pattern.",
            new("value", RuleScriptValueType.Any),
            new("pattern", RuleScriptValueType.Any));
        RegisterTyped("RegexMatches", RegexMatches, RuleScriptValueType.Array, "Returns the text of every match for a .NET regular expression pattern.",
            new("value", RuleScriptValueType.Any),
            new("pattern", RuleScriptValueType.Any));
        RegisterTyped("RegexReplace", RegexReplace, RuleScriptValueType.String, "Replaces every match for a .NET regular expression pattern. The replacement can refer to groups as $1.",
            new("value", RuleScriptValueType.Any),
            new("pattern", RuleScriptValueType.Any),
            new("replacement", RuleScriptValueType.Any));
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
        RegisterTyped("Range", Range, RuleScriptValueType.Array, "Returns the integers from start up to, but not including, end.",
            new("start", RuleScriptValueType.Number),
            new("end", RuleScriptValueType.Number));
        RegisterTyped("Range", Range, RuleScriptValueType.Array, "Returns the integers from start up to, but not including, end, counting by step. A negative step counts down.",
            new("start", RuleScriptValueType.Number),
            new("end", RuleScriptValueType.Number),
            new("step", RuleScriptValueType.Number));
        RegisterTyped("ArrayClear", ArrayClear, RuleScriptValueType.Array, "Removes all items from an array and returns the same array.",
            new RuleScriptParameterSymbol("array", RuleScriptValueType.Array));
        RegisterTyped("ArraySlice", ArraySlice, RuleScriptValueType.Array, "Returns a new array with the items from start to the end of the array.",
            new("array", RuleScriptValueType.Any),
            new("start", RuleScriptValueType.Any));
        RegisterTyped("ArraySlice", ArraySlice, RuleScriptValueType.Array, "Returns a new array with the items from start up to, but not including, end.",
            new("array", RuleScriptValueType.Any),
            new("start", RuleScriptValueType.Any),
            new("end", RuleScriptValueType.Any));
        RegisterTyped("ArrayIndexOf", ArrayIndexOf, RuleScriptValueType.Number, "Returns the zero-based index of the first matching value in an array, or -1 when there is none.",
            new("array", RuleScriptValueType.Any),
            new("value", RuleScriptValueType.Any));
        RegisterTyped("ArrayReverse", ArrayReverse, RuleScriptValueType.Array, "Reverses an array in place and returns the same array.",
            new RuleScriptParameterSymbol("array", RuleScriptValueType.Any));
        RegisterTyped("ArrayDistinct", ArrayDistinct, RuleScriptValueType.Array, "Returns a new array without duplicate values, keeping the first occurrence of each.",
            new RuleScriptParameterSymbol("array", RuleScriptValueType.Any));
        RegisterTyped("ObjectKeys", ObjectKeys, RuleScriptValueType.Array, "Returns the object's keys sorted by ordinal name.",
            new RuleScriptParameterSymbol("object", RuleScriptValueType.Object));
        RegisterTyped("ObjectContainsKey", ObjectContainsKey, RuleScriptValueType.Boolean, "Returns whether an object contains the specified string key.",
            new("object", RuleScriptValueType.Object),
            new("key", RuleScriptValueType.String));
        RegisterTyped("ObjectRemove", ObjectRemove, RuleScriptValueType.Boolean, "Removes a key from an object and returns whether it was present.",
            new("object", RuleScriptValueType.Any),
            new("key", RuleScriptValueType.Any));
        RegisterTyped("ObjectMerge", ObjectMerge, RuleScriptValueType.Object, "Returns a new object with the keys of both objects. Keys in second replace keys in first.",
            new("first", RuleScriptValueType.Any),
            new("second", RuleScriptValueType.Any));
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
        RegisterTyped("Pow", Pow, RuleScriptValueType.Number, "Returns value raised to the specified power.",
            new("value", RuleScriptValueType.Any),
            new("power", RuleScriptValueType.Any));
        RegisterTyped("Sqrt", Sqrt, RuleScriptValueType.Number, "Returns the square root of a non-negative number.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("Sum", Sum, RuleScriptValueType.Number, "Returns the sum of an array of numbers. An empty array sums to 0.",
            new RuleScriptParameterSymbol("values", RuleScriptValueType.Any));
        RegisterTyped("Average", Average, RuleScriptValueType.Number, "Returns the average of a non-empty array of numbers.",
            new RuleScriptParameterSymbol("values", RuleScriptValueType.Any));
        RegisterTyped("Now", DateFunctions.Now, RuleScriptValueType.String, "Returns the host's local date and time as an ISO 8601 string without an offset.");
        RegisterTyped("UtcNow", DateFunctions.UtcNow, RuleScriptValueType.String, "Returns the current UTC date and time as an ISO 8601 string ending in Z.");
        RegisterTyped("Today", DateFunctions.Today, RuleScriptValueType.String, "Returns the host's local date as an ISO 8601 date string, such as 2026-10-22.");
        RegisterTyped("DateAdd", DateFunctions.DateAdd, RuleScriptValueType.String, "Adds an amount of 'year', 'month', 'day', 'hour', 'minute', 'second', or 'millisecond' to an ISO 8601 date.",
            new("date", RuleScriptValueType.Any),
            new("amount", RuleScriptValueType.Any),
            new("unit", RuleScriptValueType.Any));
        RegisterTyped("DateDiff", DateFunctions.DateDiff, RuleScriptValueType.Number, "Returns end minus start in 'day', 'hour', 'minute', 'second', or 'millisecond' units, including fractions.",
            new("start", RuleScriptValueType.Any),
            new("end", RuleScriptValueType.Any),
            new("unit", RuleScriptValueType.Any));
        RegisterTyped("DatePart", DateFunctions.DatePart, RuleScriptValueType.Number, "Returns the 'year', 'month', 'day', 'hour', 'minute', 'second', 'millisecond', 'dayOfWeek' (0 is Sunday), or 'dayOfYear' of an ISO 8601 date.",
            new("date", RuleScriptValueType.Any),
            new("part", RuleScriptValueType.Any));
        RegisterTyped("FormatDate", DateFunctions.FormatDate, RuleScriptValueType.String, "Formats an ISO 8601 date with a .NET date format string, such as \"yyyy/MM/dd HH:mm\", using invariant culture.",
            new("date", RuleScriptValueType.Any),
            new("format", RuleScriptValueType.Any));
        RegisterTyped("ParseDate", DateFunctions.ParseDate, RuleScriptValueType.String, "Validates an ISO 8601 date string and returns it in normalized form.",
            new RuleScriptParameterSymbol("value", RuleScriptValueType.Any));
        RegisterTyped("ParseDate", DateFunctions.ParseDate, RuleScriptValueType.String, "Parses text with a .NET date format string, such as \"yyyy/MM/dd HH:mm\", and returns an ISO 8601 date string.",
            new("value", RuleScriptValueType.Any),
            new("format", RuleScriptValueType.Any));
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

    internal IReadOnlyCollection<RuleScriptFunctionSymbol> Signatures =>
        _signatureSnapshot ??= _signatures.Values.SelectMany(overloads => overloads).ToArray();

    public void Register(string name, Func<IReadOnlyList<RuntimeValue>, RuntimeValue> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _functions[name] = function ?? throw new ArgumentNullException(nameof(function));
        _signatures.Remove(name);
        _signatureSnapshot = null;
    }

    private void RegisterTyped(
        string name,
        Func<IReadOnlyList<RuntimeValue>, RuntimeValue> function,
        RuleScriptValueType returnType,
        string documentation,
        params RuleScriptParameterSymbol[] parameters)
    {
        if (!_signatures.TryGetValue(name, out var overloads))
        {
            Register(name, function);
            overloads = [];
            _signatures[name] = overloads;
        }

        _signatureSnapshot = null;
        overloads.Add(new RuleScriptFunctionSymbol(
            name,
            parameters,
            returnType,
            isReturnTypeNullable: false,
            isExported: false,
            documentation: documentation,
            kind: RuleScriptFunctionKind.Builtin,
            builtinMetadata: new RuleScriptBuiltinFunctionMetadata()));
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

    private static RuntimeValue Range(IReadOnlyList<RuntimeValue> arguments)
    {
        if (arguments.Count is not (2 or 3))
        {
            throw new RuntimeException($"Range expects 2 or 3 arguments but received {arguments.Count}.");
        }

        var start = RequireInt("Range", "start", arguments[0].Value);
        var end = RequireInt("Range", "end", arguments[1].Value);
        var step = arguments.Count == 3 ? RequireInt("Range", "step", arguments[2].Value) : 1;

        if (step == 0)
        {
            throw new RuntimeException("Range argument 'step' cannot be 0.");
        }

        // long arithmetic keeps ranges near int.MinValue and int.MaxValue from overflowing.
        long distance = step > 0 ? (long)end - start : (long)start - end;
        long stepSize = Math.Abs((long)step);
        var count = distance <= 0 ? 0 : (distance + stepSize - 1) / stepSize;

        if (count > MaxRangeLength)
        {
            throw new RuntimeException($"Range would create {count} items, but the maximum is {MaxRangeLength}.");
        }

        var values = new List<object?>((int)count);
        for (var i = 0; i < count; i++)
        {
            values.Add((int)(start + (long)i * step));
        }

        return new RuntimeValue(values);
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

    private static RuntimeValue IndexOf(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("IndexOf", arguments, 2);
        return new RuntimeValue(ConvertToString(arguments[0].Value).IndexOf(ConvertToString(arguments[1].Value), StringComparison.Ordinal));
    }

    private static RuntimeValue LastIndexOf(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("LastIndexOf", arguments, 2);
        var value = ConvertToString(arguments[0].Value);
        var searchValue = ConvertToString(arguments[1].Value);

        // An empty search value matches at the end, mirroring IndexOf matching at the start.
        return new RuntimeValue(searchValue.Length == 0 ? value.Length : value.LastIndexOf(searchValue, StringComparison.Ordinal));
    }

    private static RuntimeValue PadLeft(IReadOnlyList<RuntimeValue> arguments)
    {
        var (value, totalWidth, padding) = ReadPadArguments("PadLeft", arguments);
        return new RuntimeValue(value.PadLeft(totalWidth, padding));
    }

    private static RuntimeValue PadRight(IReadOnlyList<RuntimeValue> arguments)
    {
        var (value, totalWidth, padding) = ReadPadArguments("PadRight", arguments);
        return new RuntimeValue(value.PadRight(totalWidth, padding));
    }

    private static (string Value, int TotalWidth, char Padding) ReadPadArguments(string functionName, IReadOnlyList<RuntimeValue> arguments)
    {
        if (arguments.Count is not (2 or 3))
        {
            throw new RuntimeException($"{functionName} expects 2 or 3 arguments but received {arguments.Count}.");
        }

        var totalWidth = RequireInt(functionName, "totalWidth", arguments[1].Value);
        if (totalWidth is < 0 or > MaxPadWidth)
        {
            throw new RuntimeException($"{functionName} argument 'totalWidth' must be between 0 and {MaxPadWidth}.");
        }

        var padding = ' ';
        if (arguments.Count == 3)
        {
            padding = arguments[2].Value is string { Length: 1 } text
                ? text[0]
                : throw new RuntimeException($"{functionName} argument 'padding' must be a single-character string.");
        }

        return (ConvertToString(arguments[0].Value), totalWidth, padding);
    }

    private static RuntimeValue FormatNumber(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("FormatNumber", arguments, 2);
        RequireNumber("FormatNumber", arguments[0].Value);
        var format = arguments[1].Value as string
            ?? throw new RuntimeException("FormatNumber argument 'format' must be a string value.");

        try
        {
            // Format the original value so decimals from JSON keep their precision.
            return new RuntimeValue(((IFormattable)arguments[0].Value!).ToString(format, CultureInfo.InvariantCulture));
        }
        catch (FormatException exception)
        {
            throw new RuntimeException($"FormatNumber format '{format}' is not valid.", exception);
        }
    }

    private static RuntimeValue RegexIsMatch(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("RegexIsMatch", arguments, 2);
        return RunRegex("RegexIsMatch", arguments[1].Value, regex => new RuntimeValue(regex.IsMatch(ConvertToString(arguments[0].Value))));
    }

    private static RuntimeValue RegexMatches(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("RegexMatches", arguments, 2);
        return RunRegex("RegexMatches", arguments[1].Value, regex => new RuntimeValue(regex
            .Matches(ConvertToString(arguments[0].Value))
            .Select(match => (object?)match.Value)
            .ToList()));
    }

    private static RuntimeValue RegexReplace(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("RegexReplace", arguments, 3);
        var replacement = arguments[2].Value as string
            ?? throw new RuntimeException("RegexReplace argument 'replacement' must be a string value.");
        return RunRegex("RegexReplace", arguments[1].Value, regex => new RuntimeValue(regex.Replace(ConvertToString(arguments[0].Value), replacement)));
    }

    private static RuntimeValue RunRegex(string functionName, object? pattern, Func<Regex, RuntimeValue> run)
    {
        if (pattern is not string text)
        {
            throw new RuntimeException($"{functionName} argument 'pattern' must be a string value.");
        }

        Regex regex;
        try
        {
            regex = new Regex(text, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new RuntimeException($"{functionName} pattern is not valid: {exception.Message}", exception);
        }

        try
        {
            return run(regex);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new RuntimeException($"{functionName} timed out after {RegexTimeout.TotalSeconds:0} second(s).", exception);
        }
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

        if (list.Cast<object?>().All(value => TryGetDecimal(value, out _)))
        {
            var sorted = list.Cast<object?>().OrderBy(value =>
            {
                TryGetDecimal(value, out var number);
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

    private static RuntimeValue ArraySlice(IReadOnlyList<RuntimeValue> arguments)
    {
        if (arguments.Count is not (2 or 3))
        {
            throw new RuntimeException($"ArraySlice expects 2 or 3 arguments but received {arguments.Count}.");
        }

        var list = RequireList("ArraySlice", arguments[0].Value);
        var start = RequireInt("ArraySlice", "start", arguments[1].Value);
        var end = arguments.Count == 3 ? RequireInt("ArraySlice", "end", arguments[2].Value) : list.Count;

        if (start < 0 || end > list.Count || start > end)
        {
            throw new RuntimeException("ArraySlice range is outside the array bounds.");
        }

        var values = new List<object?>(end - start);
        for (var index = start; index < end; index++)
        {
            values.Add(list[index]);
        }

        return new RuntimeValue(values);
    }

    private static RuntimeValue ArrayIndexOf(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayIndexOf", arguments, 2);
        var list = RequireList("ArrayIndexOf", arguments[0].Value);

        for (var index = 0; index < list.Count; index++)
        {
            if (AreValuesEqual(list[index], arguments[1].Value))
            {
                return new RuntimeValue(index);
            }
        }

        return new RuntimeValue(-1);
    }

    private static RuntimeValue ArrayReverse(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayReverse", arguments, 1);
        var list = RequireList("ArrayReverse", arguments[0].Value);
        CopyToList(list.Cast<object?>().Reverse().ToArray(), list);
        return arguments[0];
    }

    private static RuntimeValue ArrayDistinct(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ArrayDistinct", arguments, 1);
        var list = RequireList("ArrayDistinct", arguments[0].Value);

        // Numbers compare by value like ArrayContains, so 1 and 1.0 are duplicates.
        var seen = new HashSet<object?>();
        var values = new List<object?>();
        foreach (var value in list)
        {
            if (seen.Add(TryGetDecimal(value, out var number) ? number : value))
            {
                values.Add(value);
            }
        }

        return new RuntimeValue(values);
    }

    private static RuntimeValue ObjectRemove(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ObjectRemove", arguments, 2);
        var values = arguments[0].Value as IDictionary<string, object?>
            ?? throw new RuntimeException("ObjectRemove expects a mutable object value.");
        var key = arguments[1].Value as string
            ?? throw new RuntimeException("ObjectRemove argument 'key' must be a string value.");

        if (values.IsReadOnly)
        {
            throw new RuntimeException("ObjectRemove expects a mutable object value.");
        }

        return new RuntimeValue(values.Remove(key));
    }

    private static RuntimeValue ObjectMerge(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("ObjectMerge", arguments, 2);
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in RequireObject("ObjectMerge", arguments[0].Value))
        {
            merged[key] = value;
        }

        foreach (var (key, value) in RequireObject("ObjectMerge", arguments[1].Value))
        {
            merged[key] = value;
        }

        return new RuntimeValue(merged);
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

    private static RuntimeValue Pow(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Pow", arguments, 2);
        var value = (double)RequireNumber("Pow", arguments[0].Value);
        var power = (double)RequireNumber("Pow", arguments[1].Value);
        return new RuntimeValue(RuleScriptNumbers.FromDouble(Math.Pow(value, power), "Pow result"));
    }

    private static RuntimeValue Sqrt(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Sqrt", arguments, 1);
        var value = RequireNumber("Sqrt", arguments[0].Value);
        if (value < 0)
        {
            throw new RuntimeException("Sqrt expects a non-negative number.");
        }

        return new RuntimeValue(RuleScriptNumbers.FromDouble(Math.Sqrt((double)value), "Sqrt result"));
    }

    private static RuntimeValue Sum(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Sum", arguments, 1);
        var numbers = RequireNumbers("Sum", arguments[0].Value);
        try
        {
            return new RuntimeValue(numbers.Sum());
        }
        catch (OverflowException exception)
        {
            throw new RuntimeException("Sum result is outside the range of RuleScript numbers.", exception);
        }
    }

    private static RuntimeValue Average(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Average", arguments, 1);
        var numbers = RequireNumbers("Average", arguments[0].Value);
        if (numbers.Count == 0)
        {
            throw new RuntimeException("Average expects a non-empty array.");
        }

        try
        {
            return new RuntimeValue(numbers.Average());
        }
        catch (OverflowException exception)
        {
            throw new RuntimeException("Average intermediate sum is outside the range of RuleScript numbers.", exception);
        }
    }

    private static List<decimal> RequireNumbers(string functionName, object? value)
    {
        var numbers = new List<decimal>();
        foreach (var item in RequireList(functionName, value))
        {
            if (!TryGetDecimal(item, out var number))
            {
                throw new RuntimeException($"{functionName} expects all elements to be numbers.");
            }

            numbers.Add(number);
        }

        return numbers;
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

    private static decimal RequireNumber(string functionName, object? value)
    {
        if (TryGetDecimal(value, out var number))
        {
            return number;
        }

        throw new RuntimeException($"{functionName} expects a number value.");
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

    private static bool TryGetDecimal(object? value, out decimal number) => RuleScriptNumbers.TryGetDecimal(value, out number);

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolValue => boolValue ? "true" : "false",
            decimal number => RuleScriptNumbers.ToText(number),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool AreValuesEqual(object? left, object? right)
    {
        if (TryGetDecimal(left, out var leftNumber) && TryGetDecimal(right, out var rightNumber))
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
