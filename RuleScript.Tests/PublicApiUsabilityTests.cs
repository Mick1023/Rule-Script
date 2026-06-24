using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class PublicApiUsabilityTests
{
    [Fact]
    public void Execute_ReturnsRuntimeContext()
    {
        var engine = new RuleScriptEngine();

        var context = engine.Execute("result = 123;");

        Assert.Equal(123d, context.Get<double>("result"));
    }

    [Fact]
    public void RuntimeContext_TryGet_ReturnsStoredValue()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        var found = context.TryGet("Name", out var value);

        Assert.True(found);
        Assert.Equal("Mick", value);
    }

    [Fact]
    public void RuntimeContext_ContainsAndGetOrDefault_Work()
    {
        var context = new RuntimeContext();
        context.Set("Age", 30);

        Assert.True(context.Contains("Age"));
        Assert.Equal(30, context.GetOrDefault<int>("Age"));
        Assert.Equal("fallback", context.GetOrDefault("Missing", "fallback"));
    }

    [Fact]
    public void RuntimeContext_Clear_RemovesVariables()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        context.Clear();

        Assert.False(context.Contains("Name"));
    }

    [Fact]
    public void RuntimeContext_VariablesSnapshot_CannotMutateInternalState()
    {
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        var snapshot = context.Variables;
        Assert.Throws<NotSupportedException>(() =>
        {
            var mutable = (IDictionary<string, RuntimeValue>)snapshot;
            mutable["Name"] = new RuntimeValue("Changed");
        });

        Assert.Equal("Mick", context.Get<string>("Name"));
    }

    [Fact]
    public void RegisterFunction_OverwriteBehavior_RemainsStable()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => "first");
        engine.RegisterFunction("Value", _ => "second");

        var context = engine.Execute("result = Value();");

        Assert.Equal("second", context.Get<string>("result"));
    }

    [Fact]
    public void UnregisterFunction_RemovesHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);

        Assert.True(engine.UnregisterFunction("Value"));

        Assert.Throws<RuntimeException>(() => engine.Execute("result = Value();"));
    }

    [Fact]
    public void ClearFunctions_RemovesAllHostFunctions()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("A", _ => 1);
        engine.RegisterFunction("B", _ => 2);

        engine.ClearFunctions();

        Assert.Throws<RuntimeException>(() => engine.Execute("result = A();"));
        Assert.Throws<RuntimeException>(() => engine.Execute("result = B();"));
    }
}
