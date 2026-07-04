using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class FunctionSymbolUnificationTests
{
    [Fact]
    public void Analyze_ReturnsUnifiedFunctionSymbolsForUserHostBuiltinAndImportedFunctions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rulescript-symbols-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var importedPath = Path.Combine(directory, "math.rules");
            File.WriteAllText(
                importedPath,
                """
                /// Adds two values.
                export function Add(left: number, right: number):
                    return left + right;
                endfunction
                """);

            var engine = new RuleScriptEngine { WorkingDirectory = directory };
            engine.RegisterFunction(
                "HostRead",
                [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
                RuleScriptValueType.String,
                args => $"item-{args[0]}",
                threadSafe: true);

            var result = engine.Analyze(
                """
                import "math.rules" as math;

                /// Formats a value.
                function Format(value: number):
                    return ToString(value);
                endfunction
                """);

            var user = Assert.Single(result.Functions, function => function.Name == "Format");
            Assert.Equal(RuleScriptFunctionKind.User, user.Kind);
            Assert.Equal(RuleScriptValueType.Number, Assert.Single(user.Parameters).Type);
            Assert.Equal("Formats a value.", user.Documentation);
            Assert.NotNull(user.Location);
            Assert.NotNull(user.Range);

            var imported = Assert.Single(result.Functions, function => function.Name == "math.Add");
            Assert.Equal(RuleScriptFunctionKind.Imported, imported.Kind);
            Assert.Equal(importedPath, imported.ImportMetadata?.SourcePath);
            Assert.Equal("math", imported.ImportMetadata?.Alias);
            Assert.Equal("Add", imported.ImportMetadata?.OriginalName);
            Assert.Equal(["left", "right"], imported.Parameters.Select(parameter => parameter.Name));

            var host = Assert.Single(result.Functions, function => function.Name == "HostRead");
            Assert.Equal(RuleScriptFunctionKind.Host, host.Kind);
            Assert.True(host.HostMetadata?.IsThreadSafe);
            Assert.False(host.HostMetadata?.IsAsync);
            Assert.False(host.HostMetadata?.IsVariadic);
            Assert.Same(host.HostMetadata, host.Metadata);
            Assert.Equal(RuleScriptValueType.String, host.ReturnType);

            var builtin = Assert.Single(result.Functions, function => function.Name == "ToString");
            Assert.Equal(RuleScriptFunctionKind.Builtin, builtin.Kind);
            Assert.NotNull(builtin.BuiltinMetadata);
            Assert.Same(builtin.BuiltinMetadata, builtin.Metadata);
            Assert.Equal("Converts a value to its invariant string representation.", builtin.Documentation);

            Assert.Contains("Format", result.UserFunctionNames);
            Assert.Contains("math.Add", result.UserFunctionNames);
            Assert.Contains("HostRead", result.HostFunctionNames);
            Assert.Contains("ToString", result.BuiltinFunctionNames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ReportsLegacyHostRegistrationAsHostFunctionSymbolFallback()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Legacy", args => args.Count);

        var result = engine.Analyze("var total = Legacy(1, 2);");
        var function = Assert.Single(result.Functions, symbol => symbol.Name == "Legacy");

        Assert.Equal(RuleScriptFunctionKind.Host, function.Kind);
        Assert.Empty(function.Parameters);
        Assert.Equal(RuleScriptValueType.Unknown, function.ReturnType);
        Assert.Empty(result.HostFunctions);
        Assert.Contains("Legacy", result.HostFunctionNames);
    }

    [Fact]
    public void LegacyFunctionSymbolAdaptersRemainAvailable()
    {
#pragma warning disable CS0618
        var host = new RuleScriptHostFunctionSymbol(
            "Read",
            [new RuleScriptParameterSymbol("id", RuleScriptValueType.Number)],
            RuleScriptValueType.String,
            isAsync: true,
            isThreadSafe: true,
            documentation: "Reads by id.");
        var builtin = new RuleScriptBuiltinFunctionSymbol(
            "ToString",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.Any)],
            RuleScriptValueType.String,
            "Converts to text.");
#pragma warning restore CS0618

        var hostSymbol = host.ToFunctionSymbol();
        var builtinSymbol = builtin.ToFunctionSymbol();

        Assert.Equal(RuleScriptFunctionKind.Host, hostSymbol.Kind);
        Assert.Same(hostSymbol, host.Function);
        Assert.True(hostSymbol.HostMetadata?.IsAsync);
        Assert.True(hostSymbol.HostMetadata?.IsThreadSafe);
        Assert.Equal("Reads by id.", hostSymbol.Documentation);

        Assert.Equal(RuleScriptFunctionKind.Builtin, builtinSymbol.Kind);
        Assert.Same(builtinSymbol, builtin.Function);
        Assert.NotNull(builtinSymbol.BuiltinMetadata);
        Assert.Equal("Converts to text.", builtinSymbol.Documentation);
    }
}
