using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160Rc11HostDocumentationTests
{
    [Fact]
    public void RegisterFunction_OptionsExposeDocumentationAndTypedMetadata()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "FindPlayer",
            args => $"player-{args[0]}",
            new RuleScriptHostFunctionOptions
            {
                Parameters = [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
                ReturnType = RuleScriptValueType.String,
                ThreadSafe = true,
                Documentation = "Finds a player by ID."
            });

        var context = engine.Execute("result = FindPlayer(7);");
        var registered = Assert.Single(engine.RegisteredHostFunctions);
        var analyzed = Assert.Single(engine.Analyze(string.Empty).HostFunctions);

        Assert.Equal("player-7", context.Get<string>("result"));
        Assert.Equal("Finds a player by ID.", registered.Documentation);
        Assert.Equal(registered.Documentation, analyzed.Documentation);
        Assert.True(analyzed.IsThreadSafe);
        Assert.False(analyzed.IsVariadic);
    }

    [Fact]
    public void RegisterFunction_OptionsWithoutParametersKeepUntypedVariadicBehavior()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Count",
            args => args.Count,
            new RuleScriptHostFunctionOptions { Documentation = "Counts supplied values." });

        var context = engine.Execute("result = Count(1, \"two\", true);");
        var function = Assert.Single(engine.RegisteredHostFunctions);

        Assert.Equal(3, context.Get<int>("result"));
        Assert.True(function.IsVariadic);
        Assert.Equal(RuleScriptValueType.Unknown, function.ReturnType);
        Assert.Equal("Counts supplied values.", function.Documentation);
    }

    [Fact]
    public async Task RegisterFunctionAsync_OptionsExposeDocumentation()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync(
            "ReadAsync",
            async args =>
            {
                await Task.Yield();
                return $"item-{args[0]}";
            },
            new RuleScriptHostFunctionOptions
            {
                Parameters = [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
                ReturnType = RuleScriptValueType.String,
                Documentation = "Reads an item asynchronously."
            });

        var context = await engine.ExecuteAsync("result = ReadAsync(2);");
        var function = Assert.Single(engine.Analyze(string.Empty).HostFunctions);

        Assert.Equal("item-2", context.Get<string>("result"));
        Assert.True(function.IsAsync);
        Assert.Equal("Reads an item asynchronously.", function.Documentation);
    }

    [Fact]
    public void RegisterFunctions_AttributeExposesDocumentation()
    {
        var engine = new RuleScriptEngine();

        var count = engine.RegisterFunctions(new DocumentedHost());
        var function = Assert.Single(engine.RegisteredHostFunctions);

        Assert.Equal(1, count);
        Assert.Equal("Returns the current status.", function.Documentation);
    }

    private sealed class DocumentedHost
    {
        [RuleScriptFunction(Name = "GetStatus", Documentation = "Returns the current status.")]
        public string GetStatus() => "OK";
    }
}
