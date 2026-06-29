using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class TypedHostFunctionTests
{
    [Fact]
    public void Analyze_ReturnsTypedHostFunctionSignatureAndInfersReturnType()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Add",
            [
                new RuleScriptParameterSymbol("left", RuleScriptValueType.Number),
                new RuleScriptParameterSymbol("right", RuleScriptValueType.Number)
            ],
            RuleScriptValueType.Number,
            args => Convert.ToDouble(args[0]) + Convert.ToDouble(args[1]));

        var result = engine.Analyze("var total = Add(1, 2);");
        var function = Assert.Single(result.HostFunctions);

        Assert.Equal("Add", function.Name);
        Assert.Equal(["left", "right"], function.Parameters.Select(parameter => parameter.Name));
        Assert.All(function.Parameters, parameter => Assert.Equal(RuleScriptValueType.Number, parameter.Type));
        Assert.Equal(RuleScriptValueType.Number, function.ReturnType);
        Assert.False(function.IsAsync);
        Assert.Equal(RuleScriptValueType.Number, result.Variables.Single(variable => variable.Name == "total").Type);
    }

    [Fact]
    public void TypedHostFunction_ValidatesArgumentsBeforeCallingDelegate()
    {
        var called = false;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Double",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.Number,
            args =>
            {
                called = true;
                return Convert.ToDouble(args[0]) * 2;
            });

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("result = Double(\"wrong\");"));

        Assert.False(called);
        Assert.Contains("parameter 'value' expects number", exception.Message);
        Assert.Contains("received string", exception.Message);
    }

    [Fact]
    public void TypedHostFunction_ValidatesArgumentCount()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Echo",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => args[0]);

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("result = Echo();"));

        Assert.Contains("expects 1 argument", exception.Message);
    }

    [Fact]
    public void TypedHostFunction_ValidatesReturnType()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Read",
            [],
            RuleScriptValueType.Number,
            _ => "wrong");

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("result = Read();"));

        Assert.Contains("must return number", exception.Message);
        Assert.Contains("returned string", exception.Message);
    }

    [Fact]
    public async Task TypedAsyncHostFunction_ValidatesAndExposesSignature()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync(
            "ReadAsync",
            [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            async (args, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return $"item-{args[0]}";
            });

        var context = await engine.ExecuteAsync("result = ReadAsync(7);");
        var function = Assert.Single(engine.Analyze(string.Empty).HostFunctions);

        Assert.Equal("item-7", context.Get<string>("result"));
        Assert.True(function.IsAsync);
        Assert.Equal(RuleScriptValueType.String, function.ReturnType);
    }

    [Fact]
    public void LegacyRegistration_RemainsUnvalidatedAndRemovesOldSignature()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Count",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.Number,
            args => args.Count);
        engine.RegisterFunction("Count", args => args.Count);

        var context = engine.Execute("result = Count(\"a\", true); ");

        Assert.Equal(2, context.Get<int>("result"));
        Assert.Empty(engine.RegisteredHostFunctions);
        Assert.Contains("Count", engine.Analyze(string.Empty).HostFunctionNames);
    }

    [Fact]
    public void TypedRegistration_RejectsInvalidMetadata()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<ArgumentException>(() => engine.RegisterFunction(
            "Invalid",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Unknown)],
            RuleScriptValueType.Number,
            _ => 1));
        Assert.Throws<ArgumentException>(() => engine.RegisterFunction(
            "Duplicate",
            [
                new RuleScriptParameterSymbol("value", RuleScriptValueType.Number),
                new RuleScriptParameterSymbol("value", RuleScriptValueType.String)
            ],
            RuleScriptValueType.Number,
            _ => 1));
    }
}
