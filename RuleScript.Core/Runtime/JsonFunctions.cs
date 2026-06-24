using RuleScript.Core.Diagnostics;
using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace RuleScript.Core.Runtime;

internal static class JsonFunctions
{
    public static RuntimeValue JsonParse(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("JsonParse", arguments, 1);

        if (arguments[0].Value is not string json)
        {
            throw new RuntimeException("JsonParse expects a JSON string value.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return RuntimeValue.FromObject(ConvertElement(document.RootElement));
        }
        catch (JsonException exception)
        {
            throw new RuntimeException($"JsonParse failed: {exception.Message}", exception);
        }
    }

    public static RuntimeValue JsonStringify(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("JsonStringify", arguments, 1);

        try
        {
            return new RuntimeValue(JsonSerializer.Serialize(NormalizeForJson(arguments[0].Value)));
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RuntimeException($"JsonStringify failed: {exception.Message}", exception);
        }
    }

    public static RuntimeValue JsonGet(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("JsonGet", arguments, 2);
        var path = RequirePath("JsonGet", arguments[1].Value);

        return RuntimeValue.FromObject(ResolvePath(arguments[0].Value, path));
    }

    public static RuntimeValue JsonSet(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("JsonSet", arguments, 3);
        var path = RequirePath("JsonSet", arguments[1].Value);
        SetPath(arguments[0].Value, path, arguments[2].Value);

        return arguments[0];
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertElement)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new RuntimeException($"JsonParse unsupported JSON token '{element.ValueKind}'.")
        };
    }

    private static object? NormalizeForJson(object? value)
    {
        return value switch
        {
            null => null,
            string => value,
            bool => value,
            byte or short or int or long or float or double or decimal => value,
            Dictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForJson(pair.Value),
                StringComparer.Ordinal),
            IReadOnlyDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForJson(pair.Value),
                StringComparer.Ordinal),
            IList list => list.Cast<object?>().Select(NormalizeForJson).ToList(),
            _ => throw new RuntimeException($"JsonStringify unsupported value type '{value.GetType().Name}'.")
        };
    }

    private static object? ResolvePath(object? value, string path)
    {
        var current = value;

        foreach (var segment in SplitPath(path))
        {
            current = ResolveSegment(current, segment, path);
        }

        return current;
    }

    private static object? ResolveSegment(object? value, string segment, string path)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            if (dictionary.TryGetValue(segment, out var dictionaryValue))
            {
                return dictionaryValue;
            }

            throw new RuntimeException($"JsonGet path '{path}' was not found.");
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            if (readOnlyDictionary.TryGetValue(segment, out var dictionaryValue))
            {
                return dictionaryValue;
            }

            throw new RuntimeException($"JsonGet path '{path}' was not found.");
        }

        if (value is IList list)
        {
            var index = ParsePathIndex("JsonGet", segment, path);

            if (index < 0 || index >= list.Count)
            {
                throw new RuntimeException($"JsonGet path '{path}' array index {index} is out of range.");
            }

            return list[index];
        }

        throw new RuntimeException($"JsonGet path '{path}' cannot access segment '{segment}'.");
    }

    private static void SetPath(object? value, string path, object? newValue)
    {
        var segments = SplitPath(path);

        if (segments.Length == 0)
        {
            throw new RuntimeException("JsonSet path cannot be empty.");
        }

        var current = value;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = ResolveSegmentForSet(current, segments[i], path);
        }

        SetSegment(current, segments[^1], path, newValue);
    }

    private static object? ResolveSegmentForSet(object? value, string segment, string path)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            if (dictionary.TryGetValue(segment, out var dictionaryValue))
            {
                return dictionaryValue;
            }

            throw new RuntimeException($"JsonSet path '{path}' was not found.");
        }

        if (value is IList list)
        {
            var index = ParsePathIndex("JsonSet", segment, path);

            if (index < 0 || index >= list.Count)
            {
                throw new RuntimeException($"JsonSet path '{path}' array index {index} is out of range.");
            }

            return list[index];
        }

        throw new RuntimeException($"JsonSet path '{path}' cannot access segment '{segment}'.");
    }

    private static void SetSegment(object? value, string segment, string path, object? newValue)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            if (!dictionary.ContainsKey(segment))
            {
                throw new RuntimeException($"JsonSet path '{path}' was not found.");
            }

            dictionary[segment] = newValue;
            return;
        }

        if (value is IList list)
        {
            var index = ParsePathIndex("JsonSet", segment, path);

            if (index < 0 || index >= list.Count)
            {
                throw new RuntimeException($"JsonSet path '{path}' array index {index} is out of range.");
            }

            list[index] = newValue;
            return;
        }

        throw new RuntimeException($"JsonSet path '{path}' cannot set segment '{segment}'.");
    }

    private static string RequirePath(string functionName, object? value)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        throw new RuntimeException($"{functionName} expects a non-empty string path.");
    }

    private static string[] SplitPath(string path)
    {
        return path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int ParsePathIndex(string functionName, string segment, string path)
    {
        if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return index;
        }

        throw new RuntimeException($"{functionName} path '{path}' segment '{segment}' is not a valid array index.");
    }

    private static void EnsureArgumentCount(string name, IReadOnlyList<RuntimeValue> arguments, int expected)
    {
        if (arguments.Count != expected)
        {
            throw new RuntimeException($"Builtin function '{name}' expects {expected} argument(s), but received {arguments.Count}.");
        }
    }
}
