using RuleScript.Core.Diagnostics;
using System.Globalization;

namespace RuleScript.Core.Runtime;

/// <summary>
/// RuleScript numbers are <see cref="decimal"/> values. Host values of other numeric CLR types
/// are converted when a script computes with them.
/// </summary>
internal static class RuleScriptNumbers
{
    private const decimal NormalizingDivisor = 1.0000000000000000000000000000m;

    public static bool IsNumber(object? value)
    {
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    /// <summary>
    /// Converts a numeric value to <see cref="decimal"/>. Returns false for non-numeric values and
    /// throws for numbers that have no decimal representation, such as NaN, infinity, or 1e300.
    /// </summary>
    public static bool TryGetDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal decimalValue:
                number = decimalValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case double doubleValue:
                number = FromDouble(doubleValue, "Number");
                return true;
            case float floatValue:
                number = FromDouble(floatValue, "Number");
                return true;
            case byte or sbyte or short or ushort or uint or ulong:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>
    /// Converts a <see cref="double"/> result, such as one from <see cref="Math.Pow"/>, to a RuleScript number.
    /// </summary>
    public static decimal FromDouble(double value, string source)
    {
        if (double.IsFinite(value) && Math.Abs(value) <= (double)decimal.MaxValue)
        {
            try
            {
                return (decimal)value;
            }
            catch (OverflowException)
            {
                // The range check above rounds at the boundary, so fall through to the error.
            }
        }

        throw new RuntimeException($"{source} value {value.ToString(CultureInfo.InvariantCulture)} is outside the range of RuleScript numbers.");
    }

    /// <summary>
    /// Removes trailing zeros, so 1.50 and 1.5 produce the same text.
    /// </summary>
    public static decimal Normalize(decimal value) => value / NormalizingDivisor;

    public static string ToText(decimal value) => Normalize(value).ToString(CultureInfo.InvariantCulture);
}
