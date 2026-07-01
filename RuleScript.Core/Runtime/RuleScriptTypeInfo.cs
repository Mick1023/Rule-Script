namespace RuleScript.Core.Runtime;

internal sealed class RuleScriptTypeInfo
{
    private RuleScriptTypeInfo(
        RuleScriptValueType kind,
        RuleScriptTypeInfo? elementType = null,
        IReadOnlyDictionary<string, RuleScriptTypeInfo>? properties = null)
    {
        Kind = kind;
        ElementType = elementType;
        Properties = properties;
    }

    public RuleScriptValueType Kind { get; }

    public RuleScriptTypeInfo? ElementType { get; }

    public IReadOnlyDictionary<string, RuleScriptTypeInfo>? Properties { get; }

    public static RuleScriptTypeInfo Unknown { get; } = new(RuleScriptValueType.Unknown);

    public static RuleScriptTypeInfo From(RuleScriptValueType kind)
    {
        return kind == RuleScriptValueType.Unknown ? Unknown : new RuleScriptTypeInfo(kind);
    }

    public static RuleScriptTypeInfo FromValue(object? value)
    {
        return From(RuleScriptTypeFacts.FromValue(value));
    }

    public static RuleScriptTypeInfo CreateArray(IEnumerable<RuleScriptTypeInfo> elements)
    {
        RuleScriptTypeInfo? elementType = null;

        foreach (var candidate in elements)
        {
            if (elementType is null)
            {
                elementType = candidate;
                continue;
            }

            if (elementType.Kind != candidate.Kind)
            {
                elementType = Unknown;
                break;
            }
        }

        return new RuleScriptTypeInfo(RuleScriptValueType.Array, elementType ?? Unknown);
    }

    public static RuleScriptTypeInfo CreateObject(IEnumerable<KeyValuePair<string, RuleScriptTypeInfo>> properties)
    {
        return new RuleScriptTypeInfo(
            RuleScriptValueType.Object,
            properties: properties.ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal));
    }

    public bool TryGetProperty(string name, out RuleScriptTypeInfo propertyType)
    {
        if (Properties is not null && Properties.TryGetValue(name, out var value))
        {
            propertyType = value;
            return true;
        }

        propertyType = Unknown;
        return false;
    }
}
