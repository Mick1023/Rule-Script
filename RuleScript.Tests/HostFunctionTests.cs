using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class HostFunctionTests
{
    [Fact]
    public void HostFunction_CanBeCalledFromScript()
    {
        var engine = new RuleScriptEngine();
        var called = false;
        engine.RegisterFunction("Ping", _ =>
        {
            called = true;
            return null;
        });

        engine.Execute("Ping();", new RuntimeContext());

        Assert.True(called);
    }

    [Fact]
    public void HostFunction_ReturnValueCanBeAssignedToVariable()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetDistance", _ => 519);
        var context = Execute(engine, "distance = GetDistance();");

        Assert.Equal(519, context.Get<int>("distance"));
    }

    [Fact]
    public void HostFunction_ReturnValueCanBeUsedInExpression()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetDistance", _ => 519);
        var context = Execute(engine, "result = GetDistance() + 1;");

        Assert.Equal(520d, context.Get<double>("result"));
    }

    [Fact]
    public void HostFunction_CanReceiveEvaluatedArguments()
    {
        var engine = new RuleScriptEngine();
        IReadOnlyList<object?>? received = null;
        engine.RegisterFunction("Capture", args =>
        {
            received = args;
            return args[0];
        });

        var context = Execute(engine, """
            var a = 10;
            result = Capture(a + 5, "OK");
            """);

        Assert.Equal(15d, context.Get<double>("result"));
        Assert.NotNull(received);
        Assert.Equal(15d, received[0]);
        Assert.Equal("OK", received[1]);
    }

    [Fact]
    public void HostFunction_CanOverrideBuiltInFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("ToString", _ => "host");
        var context = Execute(engine, "result = ToString(123);");

        Assert.Equal("host", context.Get<string>("result"));
    }

    [Fact]
    public void UnknownFunction_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<RuntimeException>(() => Execute(engine, "Missing();"));
    }

    [Fact]
    public void HostFunctionException_IsWrappedInRuntimeException()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Fail", _ => throw new InvalidOperationException("boom"));

        var exception = Assert.Throws<RuntimeException>(() => Execute(engine, "Fail();"));
        Assert.Contains("Fail", exception.Message);
    }

    [Fact]
    public void RegisterFunction_RejectsEmptyName()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<ArgumentException>(() => engine.RegisterFunction(" ", _ => null));
    }

    [Fact]
    public void RegisterFunction_OverwritesExistingFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);
        engine.RegisterFunction("Value", _ => 2);
        var context = Execute(engine, "result = Value();");

        Assert.Equal(2, context.Get<int>("result"));
    }

    [Fact]
    public void HostFunction_CanBeUsedInsideIfConditionWorkflow()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("IsAlarm", _ => true);
        var context = Execute(engine, """
            if IsAlarm() then:
                result = "NG";
            else:
                result = "OK";
            endif
            """);

        Assert.Equal("NG", context.Get<string>("result"));
    }

    [Fact]
    public void HostFunction_ObjectReturnValue_CanBeAssigned()
    {
        var engine = new RuleScriptEngine();
        var value = new object();
        engine.RegisterFunction("GetObject", _ => value);

        var context = Execute(engine, "result = GetObject();");

        Assert.Same(value, context.Get("result"));
    }

    [Fact]
    public void HostFunctionWorkflow_WithBuiltins_ReturnsNg()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetSensorText", _ => "SR,01,519");
        var context = Execute(engine, """
            var raw = GetSensorText();
            var distanceText = Substring(raw, 6, 3);
            var distance = ParseInt(distanceText);

            if distance > 500 then:
                result = "NG";
            else:
                result = "OK";
            endif
            """);

        Assert.Equal("NG", context.Get<string>("result"));
    }

    private static RuntimeContext Execute(RuleScriptEngine engine, string script)
    {
        var context = new RuntimeContext();

        engine.Execute(script, context);

        return context;
    }
}
