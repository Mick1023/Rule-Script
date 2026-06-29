# RuleScript

[![Build](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml/badge.svg)](https://github.com/Mick1023/Rule-Script/actions/workflows/build.yml)
[![Stable Version](https://img.shields.io/badge/stable-v1.0.0-blue)](https://github.com/Mick1023/Rule-Script/releases/tag/v1.0.0)
[![NuGet Version](https://img.shields.io/nuget/v/RuleScript.Core.svg)](https://www.nuget.org/packages/RuleScript.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RuleScript.Core.svg)](https://www.nuget.org/packages/RuleScript.Core/)

RuleScript is a lightweight, embeddable .NET 8 scripting engine for rule-oriented workflows, automation, validation, JSON processing, and host-controlled execution.

It provides a lexer, parser, AST, interpreter, built-in functions, user functions, project imports, Host Function integration, runtime diagnostics, and debugger APIs without exposing a full general-purpose programming language.

## Installation

Install the stable package from NuGet:

```bash
dotnet add package RuleScript.Core --version 1.0.0
```

## Quick Start

```csharp
using RuleScript.Core.Runtime;

var engine = new RuleScriptEngine();
var context = new RuntimeContext();

context.Set("Name", "Mick");
context.Set("Distance", 519);

engine.Execute("""
    var message = "Hello " + Name;

    if Distance > 500 then:
        result = message + ": NG";
    else:
        result = message + ": OK";
    endif
    """, context);

Console.WriteLine(context.Get<string>("result"));
```

Run a project file:

```csharp
var engine = new RuleScriptEngine
{
    WorkingDirectory = @"C:\rules"
};

var context = engine.ExecuteFile("main"); // Resolves main.rules
```

Register a Host Function:

```csharp
engine.RegisterFunction("GetDistance", _ => 519);

var context = engine.Execute("result = GetDistance();");
```

## Documentation

Detailed syntax, Host integration, debugging, analysis, and version-specific behavior are maintained in the [RuleScript Wiki](https://github.com/Mick1023/Rule-Script/wiki).

- [v1.0.0 Stable Documentation](https://github.com/Mick1023/Rule-Script/wiki/v1.0.0-Overview)
- [v1.0.0 Language Guide](https://github.com/Mick1023/Rule-Script/wiki/v1.0.0-Language-Guide)
- [v1.0.0 Built-in Function Reference](https://github.com/Mick1023/Rule-Script/wiki/v1.0.0-Built-in-Functions)
- [v1.1.0 Development Documentation](https://github.com/Mick1023/Rule-Script/wiki/v1.1.0-Development)

## Development

The unreleased v1.1.0 work is available on [`feature/v1.1.0`](https://github.com/Mick1023/Rule-Script/tree/feature/v1.1.0).

```bash
dotnet build RuleScript.sln
dotnet test RuleScript.sln
```

## License

RuleScript is licensed under the [MIT License](LICENSE).
