# RuleScript

[![Build](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml/badge.svg)](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml)
[![Version](https://img.shields.io/badge/version-v0.5.0-blue)](docs/releases/v0.5.0.md)

RuleScript is a lightweight embeddable DSL / rule engine for content modification, conditional checks, and basic numeric operations.

The project currently provides a .NET 8 class library with lexer, parser, AST, interpreter, built-in functions, host function registration, runtime context APIs, JSON helpers, and diagnostics.

## Projects

- `RuleScript.Core`: Lexer, parser, AST, runtime, built-in functions, host function API, JSON helpers, diagnostics.
- `RuleScript.Tests`: Unit, integration, diagnostics, and regression tests.

## Installation

Install from NuGet.org:

```bash
dotnet add package RuleScript.Core
```

The NuGet package is produced by GitHub Actions and can be published from tagged releases using the repository secret `NUGET_API_KEY`.

For local development, reference the core project from another .NET 8 project:

```xml
<ProjectReference Include="path\to\RuleScript.Core\RuleScript.Core.csproj" />
```

Or reference the compiled `RuleScript.Core.dll` from your host application.

## Basic Usage

```csharp
var engine = new RuleScriptEngine();
var context = new RuntimeContext();

context.Set("Name", "Mick");
context.Set("Age", 30);

engine.Execute("""
    var template = "Hello {Name}";
    result = Replace(template, "{Name}", Name);
    """, context);

var result = context.Get("result");
```

You can also let the engine create a context:

```csharp
var context = engine.Execute("""
    var a = 10;
    var b = 20;
    result = a + b;
    """);
```

## Supported Syntax

- Single-line comments with `//`
- `var` declarations
- Variable assignment
- `number`, `string`, and `bool` values
- Arithmetic operators: `+`, `-`, `*`, `/`, `%`
- Comparison operators: `>`, `>=`, `<`, `<=`, `==`, `!=`
- Grouping with `( expression )`
- Unary operators: `!`, `-`
- `if` / `else` / `endif`
- `while` / `endwhile`
- `break`
- `continue`
- `foreach` / `endforeach`
- Array literals: `[1, 2, 3]`
- Array index access: `values[0]`
- Object property access: `robot.Status`, `robot.Position.X`
- Function calls in expression statements and expressions

Example:

```rulescript
var raw = "SR,01,519";
var distanceText = Substring(raw, 6, 3);
var distance = ParseInt(distanceText);

if distance > 500 then:
    result = "NG";
else:
    result = "OK";
endif
```

Loop example:

```rulescript
var i = 0;

while i < 5:
    i = i + 1;
endwhile

result = i;
```

`break;` exits the nearest `while` loop. `continue;` skips the rest of the current loop iteration and continues the nearest `while` loop.

`break;` and `continue;` also work inside `foreach`, always targeting the nearest active loop.

`RuleScriptEngine.MaxLoopIterations` protects hosts from accidental infinite loops. The default is `100000`, and it applies to both `while` and `foreach`.

```csharp
var engine = new RuleScriptEngine
{
    MaxLoopIterations = 100000
};
```

Array and property access example:

```rulescript
var values = [1, 2, 3];
result = values[0];

var robot = GetRobot();

if robot.Status == "Error" then:
    Alarm(robot.Message);
endif
```

Arrays are represented internally as `List<object?>`. Property access supports `Dictionary<string, object?>`, `IReadOnlyDictionary<string, object?>`, and public C# object properties. Dictionary keys are checked before public properties.

Foreach example:

```rulescript
var values = [1, 2, 3];
var sum = 0;

foreach item in values:
    sum = sum + item;
endforeach

result = sum;
```

`foreach` supports `List<object?>`, `object?[]`, `IEnumerable<object?>`, and `string`. Strings iterate as one-character strings.

JSON example:

```rulescript
var obj = JsonParse("{ \"name\": \"Mick\", \"items\": [1, 2, 3] }");

result = obj.name;
second = obj.items[1];
```

JSON values integrate with property access, index access, and `foreach`:

```rulescript
var obj = JsonParse("{ \"items\": [1, 2, 3] }");
var sum = 0;

foreach item in obj.items:
    sum = sum + item;
endforeach

result = sum;
```

## RuntimeContext

`RuntimeContext` stores variables shared between the host and script.

```csharp
var context = new RuntimeContext();

context.Set("Name", "Mick");
context.Contains("Name");
context.TryGet("Name", out var value);
context.GetOrDefault("Missing", "fallback");
context.Clear();
```

`context.Variables` returns a read-only snapshot. Mutating the snapshot cannot change the internal runtime state.

## Built-in Functions

RuleScript function names are case-sensitive, matching variable names.

- `Print(value)`: returns `value`. It is currently a no-op output hook.
- `ToString(value)`: converts `value` to string. `null` converts to an empty string.
- `ParseInt(value)`: converts a string or whole number to `int`.
- `ParseDecimal(value)`: converts a string or number to `decimal`.
- `Trim(value)`: converts `value` to string and trims whitespace.
- `ToUpper(value)`: converts `value` to string and uppercases it using invariant culture.
- `ToLower(value)`: converts `value` to string and lowercases it using invariant culture.
- `Replace(value, oldValue, newValue)`: converts all arguments to string and replaces exact text matches.
- `Substring(value, start, length)`: converts `value` to string and extracts a range. `start` and `length` must be int values.
- `Length(value)`: returns array count for array values, otherwise converts `value` to string and returns its length.
- `JsonParse(value)`: converts a JSON string into RuleScript values. JSON objects become `Dictionary<string, object?>`, arrays become `List<object?>`, and numbers become `decimal`.
- `JsonStringify(value)`: converts supported RuleScript values to compact JSON.
- `JsonGet(value, path)`: reads a value by dot path, including array indexes such as `items.0.name`.
- `JsonSet(value, path, newValue)`: updates an existing path and returns the original object. Dictionaries and lists are mutated in place.

Invalid function names, wrong argument counts, invalid conversions, and invalid substring ranges throw `RuntimeException`.

JSON functions do not add JSON literal syntax to RuleScript. Use strings plus `JsonParse` when a script needs JSON data.

## Host Functions

C# host code can register custom functions on `RuleScriptEngine`:

```csharp
var engine = new RuleScriptEngine();

engine.RegisterFunction("GetDistance", args => 519);

engine.RegisterFunction("Alarm", args =>
{
    var message = args[0]?.ToString();
    return null;
});

var context = engine.Execute("""
    var distance = GetDistance();

    if distance > 500 then:
        Alarm("Too far");
        result = "NG";
    else:
        result = "OK";
    endif
    """);
```

Function lookup checks host functions first, then built-in functions, so host functions can override built-ins. Registering the same name again overwrites the previous host function.

Host function management:

```csharp
engine.RegisterFunction("Value", args => 1);
engine.UnregisterFunction("Value");
engine.ClearFunctions();
```

Host functions receive evaluated arguments as `IReadOnlyList<object?>` and return `object?`. Current supported return values are `number`, `string`, `bool`, and `null`. Other return types throw `RuntimeException`.

Timing and external wait behavior should be implemented as host functions instead of syntax:

```csharp
engine.RegisterFunction("Delay", args =>
{
    Thread.Sleep(Convert.ToInt32(args[0]));
    return null;
});

engine.RegisterFunction("WaitFor", args =>
{
    var state = args[0]?.ToString();
    var timeoutMs = Convert.ToInt32(args[1]);
    return true;
});
```

## Diagnostics

RuleScript errors use `RuleScriptException` as the base type. `SyntaxException` and `RuntimeException` include `Line`, `Column`, and `TokenText` when that information is available.

Example messages:

```text
Line 3, Column 12: Unterminated string literal.
Line 5, Column 8: Expected ';' after variable declaration. Unexpected token 'if'.
Line 4, Column 10: Runtime error: Undefined variable 'distance'.
Line 2, Column 9: Runtime error: Function 'Missing' is not registered.
Runtime error: Builtin function 'ToString' expects 1 argument(s), but received 2.
```

## Not Supported Yet

- `switch`
- JSON/object literal syntax
- User-defined script functions
- Async host functions
- Built-in `delay` / `waitfor` syntax
- Multi-error parser recovery
- Multi-token source ranges

## Milestone Status

- M1 Foundation: complete
- M2 Parser: complete
- M3 Interpreter: complete
- M3.5 Runtime validation: complete
- M4 Built-in functions: complete
- M5 Host Function API: complete
- M6 Diagnostics: complete
- M7 Rule Engine Hardening: complete
- M10 Control Flow: complete
- M11 Collections and Property Access: complete
- M12 foreach: complete
- M13 JSON Support: complete

## Verification

Run:

```powershell
dotnet build RuleScript.sln
dotnet test RuleScript.sln
```
