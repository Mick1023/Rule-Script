# Migrating to RuleScript 2.0

RuleScript 2.0 computes with `decimal` instead of `double`. Scripts get exact base-10 arithmetic, so `0.1 + 0.2 == 0.3` is `true`, and arithmetic errors stop the script instead of producing infinity. This guide lists what a host application should check when it upgrades from 1.x.

Scripts themselves need no changes unless they relied on division by zero producing infinity.

## What Changed

| | 1.x | 2.0 |
|---|---|---|
| Number literals | `double` | `decimal` |
| `+`, `-`, `*`, `/`, `%`, comparisons | Computed as `double` | Computed as `decimal` |
| `0.1 + 0.2` | `0.30000000000000004` | `0.3` |
| `1 / 0`, `5 % 0` | Infinity or NaN | Throws `RuntimeException`: `Division by zero.` |
| Result outside about ±7.9 × 10²⁸ | Infinity | Throws `RuntimeException` |
| Numbers converted to text | Shortest `double` text | Trailing zeros removed: `1.50` → `1.5`, `2.0` → `2` |
| JSON numbers in arithmetic | Converted to `double` | Stay `decimal` |
| Number-returning built-ins (`Abs`, `Min`, `Max`, `Clamp`, `Round`, `Floor`, `Ceiling`, `Pow`, `Sqrt`, `Sum`, `Average`, `DateDiff`) | `double` | `decimal` |
| `RuntimeContext.Get<T>()` on a number of another type | `InvalidCastException` | Converts |

Built-ins that return counts or positions, such as `Length`, `IndexOf`, `ArrayIndexOf`, `ParseInt`, `DatePart`, and `Range`, still return `int`.

## Checklist for Hosts

### 1. Reading numbers from `RuntimeContext`

`Get<T>()` and `GetOrDefault<T>()` now convert between numeric types, so these keep working:

```csharp
var count = context.Get<int>("count");       // decimal 3 → 3
var ratio = context.Get<double>("ratio");    // decimal 0.25 → 0.25
var total = context.Get<decimal>("total");   // no conversion
```

Reading a number with a fractional part as an integer type throws `InvalidCastException` instead of rounding; `GetOrDefault<int>` returns the default. A value too large for the target type throws `OverflowException`.

Check code that reads values without `Get<T>()`:

```csharp
// Breaks in 2.0: the value is a boxed decimal.
var ratio = (double)context.Get("ratio")!;
if (context.Get("ratio") is double value) { ... }

// Works in 1.x and 2.0.
var ratio = context.Get<double>("ratio");
var ratio = Convert.ToDouble(context.Get("ratio"), CultureInfo.InvariantCulture);
```

### 2. Host Functions

Arguments computed by a script now arrive as `decimal`. Functions registered with `RegisterFunction(name, args => ...)` receive the raw values:

```csharp
// Breaks in 2.0.
engine.RegisterFunction("SetSpeed", args => robot.SetSpeed((double)args[0]!));

// Works in 1.x and 2.0.
engine.RegisterFunction("SetSpeed", args => robot.SetSpeed(Convert.ToDouble(args[0], CultureInfo.InvariantCulture)));
```

Methods registered with `RegisterFunctions(host)` and `[RuleScriptFunction]` are unaffected: arguments are converted to the method's parameter types, as before.

Host Functions may keep returning `int`, `double`, or any other numeric type. The value is converted to `decimal` when a script computes with it.

### 3. Values passed into scripts

`int`, `long`, `double`, and other numeric values set with `RuntimeContext.Set` or returned by Host Functions keep their type until a script computes with them. At that point they are converted to `decimal`. A `double` that is NaN, infinite, or outside the `decimal` range then stops the script with `Number value ... is outside the range of RuleScript numbers.`

A `double` converts with 15 significant digits, so `0.1d` becomes exactly `0.1m`.

### 4. Displayed text

Text produced by `+`, template strings, `ToString`, `Join`, and `JsonStringify` drops trailing zeros. Values passed to Host Functions and `Print` event handlers are raw `decimal` values, and `decimal.ToString()` keeps trailing zeros. Use `FormatNumber(value, "0.00")` when a fixed number of decimals is needed.

### 5. Division

`1 / 3` is `0.3333333333333333333333333333`: 28 significant digits, rounded. `1 / 3 * 3` is therefore `0.9999999999999999999999999999`, not `1`. Round before comparing results of repeated division, for example with `Round` or `FormatNumber`.

## Precision and Range

`decimal` holds 28 to 29 significant digits in the range of about ±7.9 × 10²⁸. Literals or JSON numbers outside that range are errors.

`Pow` and `Sqrt` compute with `double` internally and convert the result, so they have `double` precision (about 15 significant digits). For heavy scientific computation, expose a Host Function that works in `double`.

## Performance

In a benchmark of 200,000 loop iterations, an arithmetic-heavy script ran in 62 ms on 2.0 versus 58 ms on 1.16.0, and a script mixing objects, comparisons, and conditions ran in the same time on both.
