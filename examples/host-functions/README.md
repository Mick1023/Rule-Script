# Host Functions Example

```csharp
var engine = new RuleScriptEngine();

engine.RegisterFunction("GetDistance", _ => 519);
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
