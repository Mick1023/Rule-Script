using System.Collections;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Describes a value type understood by RuleScript analysis and typed function parameters.
/// </summary>
public enum RuleScriptValueType
{
    Unknown,
    Any,
    Null,
    Number,
    String,
    Boolean,
    Array,
    Object,
    Void
}

internal static class RuleScriptTypeFacts
{
    public static bool TryParse(string name, out RuleScriptValueType type)
    {
        type = name.ToLowerInvariant() switch
        {
            "any" => RuleScriptValueType.Any,
            "null" => RuleScriptValueType.Null,
            "number" => RuleScriptValueType.Number,
            "string" => RuleScriptValueType.String,
            "bool" or "boolean" => RuleScriptValueType.Boolean,
            "array" => RuleScriptValueType.Array,
            "object" => RuleScriptValueType.Object,
            "void" => RuleScriptValueType.Void,
            _ => RuleScriptValueType.Unknown
        };

        return type != RuleScriptValueType.Unknown;
    }

    public static RuleScriptValueType FromValue(object? value)
    {
        return value switch
        {
            null => RuleScriptValueType.Null,
            bool => RuleScriptValueType.Boolean,
            string or char => RuleScriptValueType.String,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => RuleScriptValueType.Number,
            IDictionary => RuleScriptValueType.Object,
            IEnumerable => RuleScriptValueType.Array,
            _ => RuleScriptValueType.Object
        };
    }

    public static bool Accepts(RuleScriptValueType expected, object? value)
    {
        return expected is RuleScriptValueType.Any or RuleScriptValueType.Unknown
            || FromValue(value) == expected;
    }

    public static string ToDisplayName(RuleScriptValueType type)
    {
        return type.ToString().ToLowerInvariant();
    }
}
