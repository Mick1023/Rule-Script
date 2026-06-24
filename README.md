# RuleScript

[![Build](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml/badge.svg)](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml)
[![Version](https://img.shields.io/badge/version-v0.1.0-blue)](docs/releases/v0.1.0.md)

RuleScript is a lightweight embeddable DSL / rule engine for content modification, conditional checks, and basic numeric operations.

The project currently provides a .NET 8 class library with lexer, parser, AST, interpreter, built-in functions, host function registration, runtime context APIs, and diagnostics.

## Projects

- `RuleScript.Core`: Lexer, parser, AST, runtime, built-in functions, host function API, diagnostics.
- `RuleScript.Tests`: Unit, integration, diagnostics, and regression tests.

## Reference

Reference the core project from another .NET 8 project:

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
- `Length(value)`: converts `value` to string and returns its length.

Invalid function names, wrong argument counts, invalid conversions, and invalid substring ranges throw `RuntimeException`.

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

- Loops
- Arrays
- JSON/object literals
- User-defined script functions
- Async host functions
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

## Verification

Run:

```powershell
dotnet build RuleScript.sln
dotnet test RuleScript.sln
```
