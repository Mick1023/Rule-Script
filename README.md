# RuleScript

[![Build](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml/badge.svg)](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml)
[![Version](https://img.shields.io/badge/version-v1.1.0-blue)](docs/releases/v1.1.0.md)
[![NuGet Version](https://img.shields.io/nuget/v/RuleScript.Core.svg)](https://www.nuget.org/packages/RuleScript.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RuleScript.Core.svg)](https://www.nuget.org/packages/RuleScript.Core/)

RuleScript is a lightweight embeddable DSL / rule engine for content modification, conditional checks, and basic numeric operations.

RuleScript provides a .NET 8 class library with lexer, parser, AST, interpreter, built-in functions, host function registration, runtime context APIs, JSON helpers, and diagnostics.

## What Is RuleScript?

RuleScript is a small embeddable scripting engine for rule-oriented workflows. It is designed for host applications that need configurable content changes, validation rules, simple math, JSON processing, and project-based rule files without exposing a full general-purpose programming language.

## Installation

Install from NuGet.org:

```bash
dotnet add package RuleScript.Core
```

## Quick Start

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

Host functions can optionally register the same parameter metadata and runtime type validation used by typed user functions:

```csharp
engine.RegisterFunction(
    "Add",
    parameters:
    [
        new("left", RuleScriptValueType.Number),
        new("right", RuleScriptValueType.Number)
    ],
    returnType: RuleScriptValueType.Number,
    function: args => Convert.ToDouble(args[0]) + Convert.ToDouble(args[1]));
```

Typed registrations validate argument count and types before invoking host code, then validate the returned value. `RegisterFunctionAsync` provides equivalent overloads for both cancellation-aware and simple async delegates. Existing registrations without metadata remain unvalidated and backward compatible.

Run a project file:

```csharp
var engine = new RuleScriptEngine
{
    WorkingDirectory = @"C:\rules"
};

var context = engine.ExecuteFile("main");
var result = context.Get("result");
```

## Script Syntax

- Single-line comments with `//`
- `var` declarations
- Variable assignment
- `number`, `string`, and `bool` values
- Arithmetic operators: `+`, `-`, `*`, `/`, `%`
- Comparison operators: `>`, `>=`, `<`, `<=`, `==`, `!=`
- Boolean operators: `and`, `or`
- Grouping with `( expression )`
- Unary operators: `!`, `-`
- `if` / `else` / `end` (`endif` is also supported)
- `while` / `end` (`endwhile` is also supported)
- `break`
- `continue`
- `foreach` / `end` (`endforeach` is also supported)
- Array literals: `[1, 2, 3]`
- Array index access: `values[0]`
- Object property access: `robot.Status`, `robot.Position.X`
- Function calls in expression statements and expressions
- User-defined functions with `function` / `return` / `end` (`endfunction` is also supported)
- Explicit global access with `global.name`
- Project imports with `import "file";` and `import "file" as alias;`

## Supported Features

- Variables and assignment
- User-defined functions and `return`
- Arrays and array helpers
- `foreach`
- `while`
- JSON functions
- Import system with aliases
- Host functions

All block statements can use the same `end` keyword. The specific `endif`, `endwhile`, `endforeach`, and `endfunction` keywords remain supported for existing scripts.

```rulescript
function IsAlarm(value):
    if value > 500 then:
        return true;
    else:
        return false;
    end
end
```

## Examples

Example scripts live in:

- [basic](examples/basic/main.rules)
- [json](examples/json/main.rules)
- [modules](examples/modules/main.rules)
- [host-functions](examples/host-functions/README.md)
- [warehouse](examples/warehouse/main.rules)
- [alarm](examples/alarm/main.rules)
- [sensor](examples/sensor/main.rules)
- [workflow](examples/workflow/main.rules)

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

Multiple bool conditions can be combined with `and` and `or`:

```rulescript
if distance > 500 and enabled or forceAlarm then:
    result = "NG";
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

For long-running polling or repeated-check workflows, the limit can be disabled for both `while` and `foreach`:

```csharp
var engine = new RuleScriptEngine
{
    LoopIterationLimitEnabled = false
};
```

An unlimited loop still observes `RuleScriptEngine.Stop()` and async cancellation between iterations. Hosts should retain one of those stop mechanisms because an unconditional `while true` can otherwise run forever.

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
`context.VariableNames` returns a sorted snapshot for editor autocomplete.

## Built-in Functions

RuleScript function names are case-sensitive, matching variable names.

- `Print(value)`: returns `value`. It is currently a no-op output hook.
- `ToString(value)`: converts `value` to string. `null` converts to an empty string.
- `ParseInt(value)`: converts a string or whole number to `int`.
- `ParseDecimal(value)`: converts a string or number to `decimal`.
- `Trim(value)`: converts `value` to string and trims whitespace.
- `ToUpper(value)`: converts `value` to string and uppercases it using invariant culture.
- `ToLower(value)`: converts `value` to string and lowercases it using invariant culture.
- `StartsWith(text, value)`: checks whether text starts with value.
- `EndsWith(text, value)`: checks whether text ends with value.
- `Contains(text, value)`: checks whether text contains value.
- `Split(text, separator)`: splits text into an array.
- `Join(separator, values)`: joins an array into text.
- `Replace(value, oldValue, newValue)`: converts all arguments to string and replaces exact text matches.
- `Substring(value, start, length)`: converts `value` to string and extracts a range. `start` and `length` must be int values.
- `Length(value)`: returns array count for array values, otherwise converts `value` to string and returns its length.
- `ArrayAdd(array, value)`: appends a value and returns the array.
- `ArrayRemove(array, value)`: removes the first matching value and returns `true` if removed.
- `ArrayContains(array, value)`: checks whether an array contains a value.
- `ArrayClear(array)`: removes all items and returns the array.
- `Abs(number)`: returns absolute value.
- `Min(a, b)`: returns the smaller number.
- `Max(a, b)`: returns the larger number.
- `Clamp(value, min, max)`: clamps a number to a range.
- `Round(value)`: rounds a number.
- `Floor(value)`: floors a number.
- `Ceiling(value)`: ceilings a number.
- `ParseBool(value)`: converts a bool or bool string to `bool`.
- `IsNull(value)`: checks whether a value is `null`.
- `TypeOf(value)`: returns `null`, `string`, `bool`, `number`, `array`, or `object`.
- `Coalesce(a, b)`: returns `a` unless it is `null`; otherwise returns `b`.
- `JsonParse(value)`: converts a JSON string into RuleScript values. JSON objects become `Dictionary<string, object?>`, arrays become `List<object?>`, and numbers become `decimal`.
- `JsonStringify(value)`: converts supported RuleScript values to compact JSON.
- `JsonGet(value, path)`: reads a value by dot path, including array indexes such as `items.0.name`.
- `JsonSet(value, path, newValue)`: updates an existing path and returns the original object. Dictionaries and lists are mutated in place.
- `JsonExists(value, path)`: checks whether a JSON path exists.

Invalid function names, wrong argument counts, invalid conversions, and invalid substring ranges throw `RuntimeException`.

JSON functions do not add JSON literal syntax to RuleScript. Use strings plus `JsonParse` when a script needs JSON data.

## JSON Support

Use JSON built-ins instead of JSON literal syntax:

```rulescript
var payload = JsonParse("{ \"robot\": { \"status\": \"OK\" }, \"items\": [1, 2, 3] }");

if JsonExists(payload, "robot.status") then:
    result = payload.robot.status;
endif
```

## Project Files And Imports

Run a `.rules` file with `ExecuteFile`:

```csharp
var engine = new RuleScriptEngine
{
    WorkingDirectory = @"C:\rules"
};

var context = engine.ExecuteFile("main");
```

Relative `ExecuteFile` paths are resolved from `RuleScriptEngine.WorkingDirectory`. If `WorkingDirectory` is `null`, RuleScript uses `Environment.CurrentDirectory`. Absolute `ExecuteFile` paths ignore `WorkingDirectory`. Paths without an extension use `.rules` by default.

Hosts can choose a different script extension. Both `rule` and `.rule` are accepted and normalized to `.rule`:

```csharp
var engine = new RuleScriptEngine
{
    WorkingDirectory = @"C:\rules",
    ScriptFileExtension = ".rule"
};

var context = engine.ExecuteFile("main"); // Resolves main.rule
```

Project example:

```text
rules/
  main.rules
  common.rules
  modules/
    robot.rules
```

Global imports merge imported functions into the current file's function table:

```rulescript
import "common";

result = IsAlarm(600);
```

Alias imports keep functions isolated behind a module namespace:

```rulescript
import "robot" as robot;
import "port" as port;

var a = robot.GetSensor();
var b = port.GetSensor();

result = a + "," + b;
```

Alias imports do not expose functions globally. In the example above, `GetSensor();` throws `RuntimeException`; use `robot.GetSensor();` or `port.GetSensor();`.

Nested imports are supported, and circular imports throw `RuntimeException`. If multiple global imports provide the same function name, later global imports override earlier ones. Functions declared in the current file override globally imported functions. Alias imports are recommended when different modules use the same function names.

Import paths are resolved relative to the file that contains the import. Extensionless paths automatically use `ScriptFileExtension`; paths that already have an extension are preserved. Path normalization supports `./common` and `modules/../common`. Imported files may contain only `import` statements and function declarations; top-level executable statements are rejected in imported files.

Imported functions use the same global `RuntimeContext` as the importing program. They can read an existing variable with `global.A`, or create/update it with `global.A = value;`; the importing program and host can then read `A`. A bare top-level declaration such as `var A = 1;` is still not allowed in an imported file, so global initialization must happen inside an imported function that the main program calls.

Common import errors include missing files, duplicate aliases, unknown aliases, missing module functions, invalid alias syntax, executable statements in imported files, and circular imports. Error messages include the relevant alias, path, importing file, resolved full path, or import chain when available.

## User-Defined Functions

RuleScript functions are declared at top level:

```rulescript
function Add(a, b):
    return a + b;
endfunction

var result = Add(10, 20);
```

Function parameters can optionally declare a runtime-validated type:

```rulescript
function Format(value: number, prefix: string, enabled: bool):
    if enabled then:
        return prefix + ToString(value);
    endif
    return prefix;
endfunction
```

Supported parameter types are `number`, `string`, `bool`/`boolean`, `array`, `object`, `null`, and `any`. Untyped parameters remain backward compatible and accept any supported value. A typed call with a mismatched value throws `RuntimeException` before the function body runs.

`return expression;` returns a value. `return;` returns `null`, and a function that reaches `endfunction` without returning also returns `null`.

Functions use a local scope. Parameters and `var` declarations stay local and do not leak into `RuntimeContext`. A function can read global values from `RuntimeContext`.

Assignment inside a function always writes to the function's local scope. It does not implicitly modify global `RuntimeContext` variables.

Use `global.name` when a function must explicitly read or write a global `RuntimeContext` variable:

```rulescript
var count = 0;

function Test():
    global.count = 100;
endfunction

Test();
result = global.count;
```

`global.name` always bypasses local scope. Reads require the global variable to already exist; missing globals throw `RuntimeException`.

`return` outside a function is parsed but fails at runtime with `RuntimeException`.

Function lookup order is:

1. User-defined functions
2. Host functions
3. Built-in functions

This lets a script function override a host or built-in function name.

Recursion is intentionally blocked. A recursive user function call throws `RuntimeException`.

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

Function lookup checks user-defined functions first, then host functions, then built-in functions. Registering the same host function name again overwrites the previous host function.

Host function management:

```csharp
engine.RegisterFunction("Value", args => 1);
engine.UnregisterFunction("Value");
engine.ClearFunctions();
```

`engine.RegisteredFunctionNames` returns a sorted snapshot of every host function name. `engine.RegisteredHostFunctions` returns typed signatures, including parameters, return types, and whether each registration is async. `engine.GetVariableNames(context)` returns the current `RuntimeContext` variable names after execution.

For editor autocomplete before execution, use static analysis:

```csharp
var symbols = engine.Analyze(script);

var variables = symbols.VariableNames;
var typedVariables = symbols.Variables;       // Name + inferred RuleScriptValueType
var functions = symbols.FunctionNames;
var userFunctions = symbols.UserFunctions;   // Name + input parameter names/types
var hostFunctions = symbols.HostFunctions;   // Parameters + return type + async flag
var imports = symbols.ImportAliases;
```

`Analyze` parses the script but does not execute it. The result includes script variables, user-defined functions, currently registered host functions, built-in functions, and import aliases. It also reads imports relative to `WorkingDirectory`: globally imported functions appear by name (for example, `Shared`), while alias-imported functions appear as qualified callable names (for example, `robot.Read`). Missing import files are skipped so autocomplete remains available while a project is being edited. The script and any imported files that exist must be parseable.

For cursor-aware completion, pass a 1-based line and column. Inside a function, `VisibleVariables` includes globals, that function's parameters, and its local variables without leaking locals from other functions:

```csharp
var symbolsAtCursor = engine.Analyze(script, line, column);
var suggestions = symbolsAtCursor.VisibleVariables;
```

For live editor input that may be incomplete, use best-effort analysis:

```csharp
var attempt = engine.TryAnalyze(script);

var variables = attempt.Symbols.VariableNames;
var diagnostics = attempt.Diagnostics;
```

`TryAnalyze(script, line, column)` provides the same cursor-aware symbols for live, potentially incomplete editor input.

`TryAnalyze` does not throw for syntax errors. `Success` is `false` when parsing fails, but `Symbols` still contains names collected from the partial script when possible. Recoverable parser errors are returned together instead of stopping after the first error. Each diagnostic exposes a half-open `Range` with start and end positions for editor highlighting.

Host functions receive evaluated arguments as `IReadOnlyList<object?>` and return `object?`. Typed signatures use `RuleScriptValueType`; metadata mismatches throw `RuntimeException` with the function and parameter names.

Timing and external wait behavior should be implemented as host functions instead of syntax.
For UI applications and I/O waits, prefer async host functions with `ExecuteAsync`:

```csharp
engine.RegisterFunctionAsync("Delay", async (args, cancellationToken) =>
{
    await Task.Delay(Convert.ToInt32(args[0]), cancellationToken);
    return null;
});

engine.RegisterFunction("WaitFor", args =>
{
    var state = args[0]?.ToString();
    var timeoutMs = Convert.ToInt32(args[1]);
    return true;
});

var context = await engine.ExecuteAsync("""
    Delay(1000);
    result = "done";
    """);
```

`Execute` remains synchronous for existing hosts. If you use synchronous host functions in a UI application, run the script on a background task so the UI thread can keep rendering.

Hosts can stop the current engine execution directly:

```csharp
var runTask = engine.ExecuteAsync(script);

engine.Stop();

try
{
    await runTask;
}
catch (OperationCanceledException)
{
    // Execution was stopped by the host.
}
```

`Stop()` cancels the active `ExecuteAsync` / `ExecuteFileAsync` token, including async host functions that observe the provided cancellation token. Synchronous `Execute` / `ExecuteFile` also observe `Stop()` at statement, loop, and user-function boundaries when they are running on another thread.

## Host Runtime Notifications

Hosts can observe runtime execution through `RuleScriptEngine.RuntimeEventHandler`.
Async hosts can use `RuntimeEventHandlerAsync` with `ExecuteAsync` or `ExecuteFileAsync`.

```csharp
var engine = new RuleScriptEngine();

engine.RuntimeEventHandler = runtimeEvent =>
{
    if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print)
    {
        Console.WriteLine(runtimeEvent.Value);
    }

    if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Error)
    {
        Console.Error.WriteLine($"{runtimeEvent.Location.File}:{runtimeEvent.Location.Line}: {runtimeEvent.Message}");
    }

    return RuleScriptExecutionDirective.Continue;
};
```

Runtime events include current-line changes, `Print` calls, breakpoint hits, step pauses, and errors. Event locations include file, line, and column when available. Statement events also expose the complete multi-token source `Range` when available.

Async runtime events can await UI dispatch, logging, or external tooling work:

```csharp
engine.RuntimeEventHandlerAsync = async (runtimeEvent, cancellationToken) =>
{
    if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.Print)
    {
        await logger.WriteAsync(runtimeEvent.Value?.ToString(), cancellationToken);
    }

    return RuleScriptExecutionDirective.Continue;
};

await engine.ExecuteAsync(script);
```

Breakpoints and step-over execution are host-controlled:

```csharp
engine.AddBreakpoint(2);
engine.StepExecution = true;

engine.RuntimeEventHandler = runtimeEvent =>
{
    if (runtimeEvent.Kind == RuleScriptRuntimeEventKind.BreakpointHit ||
        runtimeEvent.Kind == RuleScriptRuntimeEventKind.StepPaused)
    {
        return RuleScriptExecutionDirective.StepOver;
    }

    return RuleScriptExecutionDirective.Continue;
};
```

`RuntimeContext.CurrentLocation` stores the last source location reported during execution.

For host tools that need to pause execution and wait for external commands, use `RuleScriptDebugSession`:

```csharp
var engine = new RuleScriptEngine
{
    WorkingDirectory = selectedFolder
};

engine.AddBreakpoint("main.rules", 2);
engine.AddBreakpoint("main.rules", 4, "retryCount >= 3");

var session = new RuleScriptDebugSession(engine);
var runTask = session.RunFileAsync("main.rules");

var pause = await session.WaitForPauseAsync();
var snapshot = session.CurrentSnapshot;
var globals = snapshot?.Globals;
var locals = snapshot?.Locals;
var callStack = snapshot?.CallStack;
session.StepOver();

pause = await session.WaitForPauseAsync();
session.Continue();

var context = await runTask;
```

A UI Stop button can cancel the current debug run, including when execution is paused:

```csharp
session.Stop();

try
{
    await runTask;
}
catch (OperationCanceledException)
{
    // Debug run was stopped by the host.
}
```

## Diagnostics

RuleScript errors use `RuleScriptException` as the base type. `SyntaxException` and `RuntimeException` include `SourceFile`, `Line`, `Column`, and `TokenText` when that information is available.

Example messages:

```text
Line 3, Column 12: Unterminated string literal.
Line 5, Column 8: Expected ';' after variable declaration. Unexpected token 'if'.
Line 4, Column 10: Runtime error: Undefined variable 'distance'.
Line 2, Column 9: Runtime error: Function 'Missing' is not registered.
Runtime error: Builtin function 'ToString' expects 1 argument(s), but received 2.
```

## Roadmap / Planned Features

- `switch`
- Object literal syntax
- Package manager
- Generic collections
- Built-in `delay` / `waitfor` syntax
- Step Into
- Step Out

