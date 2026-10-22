using RuleScript.Core.Diagnostics;
using System.Globalization;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Date and time built-ins. Scripts represent dates as ISO 8601 strings, such as
/// <c>2026-10-22T10:30:00</c> (local time), <c>2026-10-22T02:30:00Z</c> (UTC), or
/// <c>2026-10-22T10:30:00+08:00</c> (explicit offset). Results keep the input's form and use
/// millisecond precision.
/// </summary>
internal static class DateFunctions
{
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    private const string AddUnits = "'year', 'month', 'day', 'hour', 'minute', 'second', or 'millisecond'";
    private const string DiffUnits = "'day', 'hour', 'minute', 'second', or 'millisecond'";
    private const string Parts = "'year', 'month', 'day', 'hour', 'minute', 'second', 'millisecond', 'dayOfWeek', or 'dayOfYear'";

    private enum DateKind
    {
        /// <summary>No offset; read as host local time when compared with UTC or offset dates.</summary>
        Local,
        Utc,
        Offset
    }

    /// <summary>
    /// A parsed date. For <see cref="DateKind.Local"/>, <see cref="Value"/> carries the wall-clock
    /// time with a zero offset, so arithmetic never shifts across daylight saving changes.
    /// </summary>
    private readonly record struct ScriptDate(DateTimeOffset Value, DateKind Kind);

    public static RuntimeValue Now(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Now", arguments, 0);
        return new RuntimeValue(Format(new ScriptDate(new DateTimeOffset(TruncateToMilliseconds(DateTime.Now).Ticks, TimeSpan.Zero), DateKind.Local)));
    }

    public static RuntimeValue UtcNow(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("UtcNow", arguments, 0);
        return new RuntimeValue(Format(new ScriptDate(new DateTimeOffset(TruncateToMilliseconds(DateTime.UtcNow).Ticks, TimeSpan.Zero), DateKind.Utc)));
    }

    public static RuntimeValue Today(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("Today", arguments, 0);
        return new RuntimeValue(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public static RuntimeValue DateAdd(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("DateAdd", arguments, 3);
        var date = RequireDate("DateAdd", arguments[0].Value);
        var unit = RequireString("DateAdd", "unit", arguments[2].Value);

        try
        {
            var value = date.Value;
            var result = unit switch
            {
                "year" => value.AddYears(RequireWholeAmount(unit, arguments[1].Value)),
                "month" => value.AddMonths(RequireWholeAmount(unit, arguments[1].Value)),
                "day" => value.AddDays(RequireAmount(arguments[1].Value)),
                "hour" => value.AddHours(RequireAmount(arguments[1].Value)),
                "minute" => value.AddMinutes(RequireAmount(arguments[1].Value)),
                "second" => value.AddSeconds(RequireAmount(arguments[1].Value)),
                "millisecond" => value.AddMilliseconds(RequireAmount(arguments[1].Value)),
                _ => throw new RuntimeException($"DateAdd unit '{unit}' is not supported. Use {AddUnits}.")
            };

            return new RuntimeValue(Format(date with { Value = TruncateToMilliseconds(result) }));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RuntimeException("DateAdd result is outside the supported date range.", exception);
        }
    }

    public static RuntimeValue DateDiff(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("DateDiff", arguments, 3);
        var start = RequireDate("DateDiff", arguments[0].Value);
        var end = RequireDate("DateDiff", arguments[1].Value);
        var unit = RequireString("DateDiff", "unit", arguments[2].Value);

        // Two local dates compare by wall clock; otherwise local dates are placed in the host's time zone.
        var difference = start.Kind == DateKind.Local && end.Kind == DateKind.Local
            ? end.Value - start.Value
            : ToInstant(end) - ToInstant(start);

        return new RuntimeValue(unit switch
        {
            "day" => difference.TotalDays,
            "hour" => difference.TotalHours,
            "minute" => difference.TotalMinutes,
            "second" => difference.TotalSeconds,
            "millisecond" => difference.TotalMilliseconds,
            _ => throw new RuntimeException($"DateDiff unit '{unit}' is not supported. Use {DiffUnits}.")
        });
    }

    public static RuntimeValue DatePart(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("DatePart", arguments, 2);
        var value = RequireDate("DatePart", arguments[0].Value).Value;
        var part = RequireString("DatePart", "part", arguments[1].Value);

        return new RuntimeValue(part switch
        {
            "year" => value.Year,
            "month" => value.Month,
            "day" => value.Day,
            "hour" => value.Hour,
            "minute" => value.Minute,
            "second" => value.Second,
            "millisecond" => value.Millisecond,
            "dayOfWeek" => (int)value.DayOfWeek,
            "dayOfYear" => value.DayOfYear,
            _ => throw new RuntimeException($"DatePart part '{part}' is not supported. Use {Parts}.")
        });
    }

    public static RuntimeValue FormatDate(IReadOnlyList<RuntimeValue> arguments)
    {
        EnsureArgumentCount("FormatDate", arguments, 2);
        var date = RequireDate("FormatDate", arguments[0].Value);
        var format = RequireString("FormatDate", "format", arguments[1].Value);

        try
        {
            return new RuntimeValue(date.Kind switch
            {
                DateKind.Local => date.Value.DateTime.ToString(format, CultureInfo.InvariantCulture),
                DateKind.Utc => date.Value.UtcDateTime.ToString(format, CultureInfo.InvariantCulture),
                _ => date.Value.ToString(format, CultureInfo.InvariantCulture)
            });
        }
        catch (FormatException exception)
        {
            throw new RuntimeException($"FormatDate format '{format}' is not valid.", exception);
        }
    }

    public static RuntimeValue ParseDate(IReadOnlyList<RuntimeValue> arguments)
    {
        if (arguments.Count is not (1 or 2))
        {
            throw new RuntimeException($"ParseDate expects 1 or 2 arguments but received {arguments.Count}.");
        }

        var text = RequireString("ParseDate", "value", arguments[0].Value);
        if (arguments.Count == 1)
        {
            return new RuntimeValue(Format(RequireDate("ParseDate", text)));
        }

        var format = RequireString("ParseDate", "format", arguments[1].Value);
        try
        {
            if (format.Contains('z') || format.Contains('K'))
            {
                if (DateTimeOffset.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offsetValue))
                {
                    return new RuntimeValue(Format(new ScriptDate(TruncateToMilliseconds(offsetValue), DateKind.Offset)));
                }
            }
            else if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localValue))
            {
                return new RuntimeValue(Format(new ScriptDate(new DateTimeOffset(TruncateToMilliseconds(localValue).Ticks, TimeSpan.Zero), DateKind.Local)));
            }
        }
        catch (FormatException exception)
        {
            throw new RuntimeException($"ParseDate format '{format}' is not valid.", exception);
        }

        throw new RuntimeException($"ParseDate could not parse '{text}' with format '{format}'.");
    }

    private static ScriptDate RequireDate(string functionName, object? value)
    {
        switch (value)
        {
            case string text:
                if (!DateTime.TryParseExact(text, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    break;
                }

                if (parsed.Kind == DateTimeKind.Local)
                {
                    // RoundtripKind converts explicit offsets to host local time, so read the offset again.
                    var withOffset = DateTimeOffset.ParseExact(text, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
                    return new ScriptDate(TruncateToMilliseconds(withOffset), DateKind.Offset);
                }

                return new ScriptDate(
                    new DateTimeOffset(TruncateToMilliseconds(parsed).Ticks, TimeSpan.Zero),
                    parsed.Kind == DateTimeKind.Utc ? DateKind.Utc : DateKind.Local);
            case DateTime dateTime:
                return dateTime.Kind == DateTimeKind.Utc
                    ? new ScriptDate(new DateTimeOffset(TruncateToMilliseconds(dateTime).Ticks, TimeSpan.Zero), DateKind.Utc)
                    : new ScriptDate(new DateTimeOffset(TruncateToMilliseconds(dateTime).Ticks, TimeSpan.Zero), DateKind.Local);
            case DateTimeOffset dateTimeOffset:
                return new ScriptDate(TruncateToMilliseconds(dateTimeOffset), DateKind.Offset);
        }

        throw new RuntimeException($"{functionName} expects an ISO 8601 date string such as '2026-10-22T10:30:00'.");
    }

    private static DateTimeOffset ToInstant(ScriptDate date)
    {
        return date.Kind == DateKind.Local
            ? new DateTimeOffset(DateTime.SpecifyKind(date.Value.DateTime, DateTimeKind.Local))
            : date.Value;
    }

    private static string Format(ScriptDate date)
    {
        var value = date.Value;
        var text = value.ToString(value.Millisecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss" : "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return date.Kind switch
        {
            DateKind.Utc => text + "Z",
            DateKind.Offset => text + value.ToString("zzz", CultureInfo.InvariantCulture),
            _ => text
        };
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static double RequireAmount(object? value)
    {
        if (RuleScriptTypeFacts.FromValue(value) == RuleScriptValueType.Number)
        {
            var amount = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsFinite(amount))
            {
                return amount;
            }
        }

        throw new RuntimeException("DateAdd argument 'amount' must be a finite number.");
    }

    private static int RequireWholeAmount(string unit, object? value)
    {
        var amount = RequireAmount(value);
        if (amount != Math.Truncate(amount) || amount is < int.MinValue or > int.MaxValue)
        {
            throw new RuntimeException($"DateAdd argument 'amount' must be a whole number for unit '{unit}'.");
        }

        return (int)amount;
    }

    private static string RequireString(string functionName, string argumentName, object? value)
    {
        return value as string ?? throw new RuntimeException($"{functionName} argument '{argumentName}' must be a string value.");
    }

    private static void EnsureArgumentCount(string name, IReadOnlyList<RuntimeValue> arguments, int expected)
    {
        if (arguments.Count != expected)
        {
            throw new RuntimeException($"Builtin function '{name}' expects {expected} argument(s), but received {arguments.Count}.");
        }
    }
}
