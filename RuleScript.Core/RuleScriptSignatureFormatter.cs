using RuleScript.Core.Runtime;

namespace RuleScript.Core;

/// <summary>
/// Formats RuleScript-style signatures shared by hover, completion, and signature help.
/// </summary>
internal static class RuleScriptSignatureFormatter
{
    public static string FormatTyped(string name, RuleScriptValueType type)
    {
        return IsKnown(type) ? $"{name}: {RuleScriptTypeFacts.ToDisplayName(type)}" : name;
    }

    /// <summary>
    /// Formats <c>Name(a: number, b) -> string</c> and reports where each parameter label sits inside it.
    /// </summary>
    public static (string Label, IReadOnlyList<(int Start, int End)> ParameterOffsets) FormatSignature(
        string name,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType)
    {
        var builder = new System.Text.StringBuilder(name).Append('(');
        var offsets = new List<(int Start, int End)>();

        for (var index = 0; index < parameters.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var parameter = FormatTyped(parameters[index].Name, parameters[index].Type);
            offsets.Add((builder.Length, builder.Length + parameter.Length));
            builder.Append(parameter);
        }

        builder.Append(')');
        if (IsKnown(returnType))
        {
            builder.Append(" -> ").Append(RuleScriptTypeFacts.ToDisplayName(returnType));
        }

        return (builder.ToString(), offsets);
    }

    public static string GetFunctionPrefix(RuleScriptFunctionKind? kind)
    {
        return kind switch
        {
            RuleScriptFunctionKind.Host => "host function",
            RuleScriptFunctionKind.Builtin => "builtin function",
            _ => "function"
        };
    }

    public static bool IsKnown(RuleScriptValueType type) => type != RuleScriptValueType.Unknown;
}
