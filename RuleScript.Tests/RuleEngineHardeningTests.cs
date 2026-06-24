using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuleEngineHardeningTests
{
    [Fact]
    public void EndToEnd_ContentReplacement_ReplacesPlaceholder()
    {
        var context = Execute("""
            var template = "Hello {Name}";
            var name = "Mick";
            result = Replace(template, "{Name}", name);
            """);

        Assert.Equal("Hello Mick", context.Get<string>("result"));
    }

    [Fact]
    public void EndToEnd_SensorMessageParsing_ReturnsNg()
    {
        var context = Execute("""
            var raw = "SR,01,519";
            var distanceText = Substring(raw, 6, 3);
            var distance = ParseInt(distanceText);

            if distance > 500 then:
                status = "NG";
            else:
                status = "OK";
            endif
            """);

        Assert.Equal("NG", context.Get<string>("status"));
    }

    [Fact]
    public void EndToEnd_HostFunctionWorkflow_ReturnsOk()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetDistance", _ => 450);

        var context = engine.Execute("""
            var distance = GetDistance();

            if distance > 500 then:
                status = "NG";
            else:
                status = "OK";
            endif
            """);

        Assert.Equal("OK", context.Get<string>("status"));
    }

    [Fact]
    public void EndToEnd_HostAlarmFunction_RecordsMessage()
    {
        var engine = new RuleScriptEngine();
        var messages = new List<string?>();
        engine.RegisterFunction("Alarm", args =>
        {
            messages.Add(args[0]?.ToString());
            return null;
        });

        var context = engine.Execute("""
            Alarm("Too far");
            result = "done";
            """);

        Assert.Single(messages);
        Assert.Equal("Too far", messages[0]);
        Assert.Equal("done", context.Get<string>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }
}
