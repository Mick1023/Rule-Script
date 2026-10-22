using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class HostFunctionOverloadTests
{
    [Fact]
    public void Execute_DispatchesHostOverloadsByArgumentCount()
    {
        var engine = CreatePadEngine();

        var context = engine.Execute("""
            one = Pad("a");
            two = Pad("a", 3);
            """);

        Assert.Equal("[a]", context.Get<string>("one"));
        Assert.Equal("[a  ]", context.Get<string>("two"));
    }

    [Fact]
    public void Execute_DispatchesHostOverloadsByArgumentType()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Describe",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            args => $"number:{args[0]}");
        engine.RegisterFunction(
            "Describe",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => $"string:{args[0]}");

        var context = engine.Execute("""
            numberText = Describe(7);
            stringText = Describe("ok");
            """);

        Assert.Equal("number:7", context.Get<string>("numberText"));
        Assert.Equal("string:ok", context.Get<string>("stringText"));
        Assert.Equal(2, engine.RegisteredHostFunctions.Count);
    }

    [Fact]
    public void Analyze_ExposesEveryHostOverloadWithoutDiagnostics()
    {
        var engine = CreatePadEngine();

        var result = engine.Analyze("""
            var one = Pad("a");
            var two = Pad("a", 3);
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["Pad(string)", "Pad(string, number)"],
            result.HostFunctions.Select(function => function.ToFunctionSymbol().Signature).Order(StringComparer.Ordinal));
        Assert.Equal(2, result.Functions.Count(function => function.Name == "Pad"));
        Assert.Equal(RuleScriptValueType.String, result.Variables.Single(variable => variable.Name == "two").Type);
    }

    [Fact]
    public void Analyze_ReportsNoMatchingHostOverload()
    {
        var engine = CreatePadEngine();

        var result = engine.Analyze("var value = Pad(1, 2, 3);");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message == "No matching overload for function 'Pad'.");
    }

    [Fact]
    public void Execute_ReportsNoMatchingHostOverload()
    {
        var engine = CreatePadEngine();

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("value = Pad(1, 2, 3);"));

        Assert.Contains("No matching overload for function 'Pad'.", exception.Message);
    }

    [Fact]
    public void GetSignatureHelp_ListsEveryHostOverload()
    {
        var engine = CreatePadEngine();
        const string source = "var value = Pad(\"a\", ";
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, source);

        var help = RuleScriptLanguageService.GetSignatureHelp(document, 1, source.Length + 1);

        Assert.NotNull(help);
        Assert.Equal(2, help.Signatures.Count);
        Assert.Equal("Pad(text: string, width: number) -> string", help.Signatures[help.ActiveSignature].Label);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public void GetHover_SelectsHostOverloadByArgumentCount()
    {
        var engine = CreatePadEngine();
        const string source = """
            var one = Pad("a");
            var two = Pad("a", 3);
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, source);

        Assert.Equal("host function Pad(text: string) -> string", RuleScriptLanguageService.GetHover(document, 1, 11)?.Signature);
        Assert.Equal("host function Pad(text: string, width: number) -> string", RuleScriptLanguageService.GetHover(document, 2, 11)?.Signature);
    }

    [Fact]
    public void GetHoverAndDefinition_SelectHostOverloadByArgumentType()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Describe",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            args => $"number:{args[0]}");
        engine.RegisterFunction(
            "Describe",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => $"string:{args[0]}");
        const string source = """
            var a = Describe(7);
            var b = Describe("ok");
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, source);

        Assert.Equal("host function Describe(value: number) -> string", RuleScriptLanguageService.GetHover(document, 1, 9)?.Signature);
        Assert.Equal("host function Describe(value: string) -> string", RuleScriptLanguageService.GetHover(document, 2, 9)?.Signature);
        Assert.Equal(RuleScriptValueType.String, RuleScriptLanguageService.GetDefinition(document, 2, 9)?.Parameters?.Single().Type);
        Assert.Equal(
            [1],
            RuleScriptLanguageService.FindReferences(document, 1, 9)
                .Where(reference => reference.Range is not null)
                .Select(reference => reference.Range!.StartLine));
    }

    [Fact]
    public void RegisterFunction_SameSignatureReplacesOnlyThatOverload()
    {
        var engine = CreatePadEngine();
        engine.RegisterFunction(
            "Pad",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => $"<{args[0]}>");

        var context = engine.Execute("""
            one = Pad("a");
            two = Pad("a", 2);
            """);

        Assert.Equal("<a>", context.Get<string>("one"));
        Assert.Equal("[a ]", context.Get<string>("two"));
        Assert.Equal(2, engine.RegisteredHostFunctions.Count);
    }

    [Fact]
    public void RegisterFunction_UntypedRegistrationReplacesAllSyncOverloads()
    {
        var engine = CreatePadEngine();
        engine.RegisterFunction("Pad", args => args.Count);

        var context = engine.Execute("""value = Pad(true, false, 1);""");

        Assert.Equal(3, context.Get<int>("value"));
        Assert.Empty(engine.RegisteredHostFunctions);
    }

    [Fact]
    public void RegisterFunction_TypedRegistrationReplacesUntypedRegistration()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Pad", args => args.Count);
        engine.RegisterFunction(
            "Pad",
            [new RuleScriptParameterSymbol("text", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => $"[{args[0]}]");

        Assert.Throws<RuntimeException>(() => engine.Execute("value = Pad(true, false);"));
        Assert.Equal("[a]", engine.Execute("value = Pad(\"a\");").Get<string>("value"));
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesAsyncHostOverloads()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync(
            "Load",
            [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            args => Task.FromResult<object?>($"id:{args[0]}"));
        engine.RegisterFunctionAsync(
            "Load",
            [new RuleScriptParameterSymbol("key", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => Task.FromResult<object?>($"key:{args[0]}"));

        var context = await engine.ExecuteAsync("""
            byId = Load(1);
            byKey = Load("k");
            """);

        Assert.Equal("id:1", context.Get<string>("byId"));
        Assert.Equal("key:k", context.Get<string>("byKey"));
    }

    [Fact]
    public void UnregisterFunction_WithParameterTypesRemovesOnlyThatOverload()
    {
        var engine = CreatePadEngine();

        Assert.True(engine.UnregisterFunction("Pad", [RuleScriptValueType.String, RuleScriptValueType.Number]));
        Assert.False(engine.UnregisterFunction("Pad", [RuleScriptValueType.Boolean]));

        Assert.Equal("[a]", engine.Execute("value = Pad(\"a\");").Get<string>("value"));
        Assert.Throws<RuntimeException>(() => engine.Execute("value = Pad(\"a\", 2);"));

        Assert.True(engine.UnregisterFunction("Pad", [RuleScriptValueType.String]));
        Assert.DoesNotContain("Pad", engine.RegisteredFunctionNames);
    }

    [Fact]
    public void RegisterFunctions_RegistersOverloadedAttributedMethods()
    {
        var engine = new RuleScriptEngine();

        Assert.Equal(2, engine.RegisterFunctions(new OverloadedHost()));

        var context = engine.Execute("""
            one = Greet("Ann");
            two = Greet("Ann", "Hi");
            """);

        Assert.Equal("Hello, Ann", context.Get<string>("one"));
        Assert.Equal("Hi, Ann", context.Get<string>("two"));
    }

    [Fact]
    public void RegisterFunctions_RejectsOverloadsWithTheSameScriptSignature()
    {
        var engine = new RuleScriptEngine();

        var exception = Assert.Throws<NotSupportedException>(() => engine.RegisterFunctions(new CollidingHost()));

        Assert.Contains("Scale(number)", exception.Message);
    }

    private static RuleScriptEngine CreatePadEngine()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Pad",
            [new RuleScriptParameterSymbol("text", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => $"[{args[0]}]");
        engine.RegisterFunction(
            "Pad",
            [
                new RuleScriptParameterSymbol("text", RuleScriptValueType.String),
                new RuleScriptParameterSymbol("width", RuleScriptValueType.Number)
            ],
            RuleScriptValueType.String,
            args => $"[{((string)args[0]!).PadRight(Convert.ToInt32(args[1]))}]");
        return engine;
    }

    private sealed class OverloadedHost
    {
        [RuleScriptFunction]
        public string Greet(string name) => $"Hello, {name}";

        [RuleScriptFunction]
        public string Greet(string name, string greeting) => $"{greeting}, {name}";
    }

    private sealed class CollidingHost
    {
        [RuleScriptFunction]
        public decimal Scale(int value) => value * 2;

        [RuleScriptFunction]
        public decimal Scale(decimal value) => value * 2;
    }
}
