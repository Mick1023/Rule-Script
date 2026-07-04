using RuleScript.Core;
using RuleScript.Core.Formatting;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version170NavigationTests
{
    [Fact]
    public void GetDefinition_LocalFunction_ReturnsDeclarationRange()
    {
        const string source = """
            function Test():
            endfunction

            Test();
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 4, 1);

        Assert.NotNull(definition);
        Assert.Equal(("Test", RuleScriptSymbolKind.Function, false), (definition.Name, definition.Kind, definition.IsExternal));
        Assert.Equal((1, 10, 1, 14), RangeTuple(definition.SelectionRange));
    }

    [Fact]
    public void GetDefinition_ImportedFunction_ReturnsExportDeclarationRange()
    {
        using var project = new RuleScriptProject();
        project.Write("Player.rules", """
            export function UpdatePlayer():
            endfunction
            """);
        var engine = new RuleScriptEngine { WorkingDirectory = project.Directory };
        const string source = """
            import "Player";

            UpdatePlayer();
            """;

        var definition = RuleScriptLanguageService.GetDefinition(engine, source, 3, 1);

        Assert.NotNull(definition);
        Assert.Equal("UpdatePlayer", definition.Name);
        Assert.False(definition.IsExternal);
        Assert.EndsWith("Player.rules", definition.SelectionRange?.File);
        Assert.Equal((1, 17, 1, 29), RangeTuple(definition.SelectionRange));
    }


    [Fact]
    public void GetDefinition_LocalFunction_ExposesFileLineColumnProperties()
    {
        const string source = """
            function Test():
            endfunction

            Test();
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 4, 1);

        Assert.NotNull(definition);
        Assert.Null(definition.File);
        Assert.Equal(1, definition.Line);
        Assert.Equal(10, definition.Column);
        Assert.Equal(1, definition.EndLine);
        Assert.Equal(14, definition.EndColumn);
    }

    [Fact]
    public void FindReferences_ImportedFunction_ExposesFileLineColumnProperties()
    {
        using var project = new RuleScriptProject();
        project.Write("Player.rules", """
            export function UpdatePlayer():
                UpdatePlayer();
            endfunction
            """);
        var engine = new RuleScriptEngine { WorkingDirectory = project.Directory };
        const string source = """
            import "Player";

            UpdatePlayer();
            """;

        var references = RuleScriptLanguageService.FindReferences(engine, source, 3, 1);

        var importedDeclaration = Assert.Single(references, reference => reference.IsDeclaration);
        Assert.EndsWith("Player.rules", importedDeclaration.File);
        Assert.Equal(1, importedDeclaration.Line);
        Assert.Equal(17, importedDeclaration.Column);
        Assert.Equal(1, importedDeclaration.EndLine);
        Assert.Equal(29, importedDeclaration.EndColumn);

        var currentFileCall = Assert.Single(references, reference => !reference.IsDeclaration && reference.File is null);
        Assert.Null(currentFileCall.File);
        Assert.Equal(3, currentFileCall.Line);
        Assert.Equal(1, currentFileCall.Column);
    }

    [Fact]
    public void GetDefinition_HostFunction_ReturnsExternalMetadata()
    {
        const string source = """Print("Hello");""";

        var definition = RuleScriptLanguageService.GetDefinition(source, 1, 1);

        Assert.NotNull(definition);
        Assert.Equal(("Print", RuleScriptSymbolKind.HostFunction, true), (definition.Name, definition.Kind, definition.IsExternal));
        Assert.Null(definition.Range);
        Assert.Null(definition.SelectionRange);
        Assert.Single(definition.Parameters);
        Assert.Equal(RuleScriptValueType.Any, definition.ReturnType);
        Assert.Contains("supplied value", definition.Documentation);
    }

    [Fact]
    public void GetDefinition_RegisteredHostFunction_UsesUnifiedFunctionMetadata()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "HostScore",
            _ => 42,
            new RuleScriptHostFunctionOptions
            {
                Parameters = [new RuleScriptParameterSymbol("playerId", RuleScriptValueType.String)],
                ReturnType = RuleScriptValueType.Number,
                Documentation = "Reads the player's score."
            });
        const string source = """HostScore("p1");""";

        var definition = RuleScriptLanguageService.GetDefinition(engine, source, 1, 1);

        Assert.NotNull(definition);
        Assert.Equal(("HostScore", RuleScriptSymbolKind.HostFunction, true), (definition.Name, definition.Kind, definition.IsExternal));
        Assert.Single(definition.Parameters, parameter => parameter.Name == "playerId" && parameter.Type == RuleScriptValueType.String);
        Assert.Equal(RuleScriptValueType.Number, definition.ReturnType);
        Assert.Equal("Reads the player's score.", definition.Documentation);

        var symbol = Assert.Single(engine.Analyze(source).Functions, function => function.Name == "HostScore");
        Assert.Equal(RuleScriptFunctionKind.Host, symbol.Kind);
    }

    [Fact]
    public void GetDefinition_BuiltinFunction_UsesUnifiedFunctionMetadata()
    {
        const string source = """ToString(1);""";

        var definition = RuleScriptLanguageService.GetDefinition(source, 1, 1);

        Assert.NotNull(definition);
        Assert.Equal(("ToString", RuleScriptSymbolKind.HostFunction, true), (definition.Name, definition.Kind, definition.IsExternal));
        Assert.Single(definition.Parameters, parameter => parameter.Name == "value" && parameter.Type == RuleScriptValueType.Any);
        Assert.Equal(RuleScriptValueType.String, definition.ReturnType);
        Assert.Contains("invariant string", definition.Documentation);

        var symbol = Assert.Single(new RuleScriptEngine().Analyze(source).Functions, function => function.Name == "ToString");
        Assert.Equal(RuleScriptFunctionKind.Builtin, symbol.Kind);
    }

    [Fact]
    public void GetDefinition_FunctionParameter_ReturnsParameterDeclaration()
    {
        const string source = """
            function Add(a, b):
                return a + b;
            endfunction
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 2, 12);

        Assert.NotNull(definition);
        Assert.Equal(("a", RuleScriptSymbolKind.Parameter), (definition.Name, definition.Kind));
        Assert.Equal((1, 14, 1, 15), RangeTuple(definition.SelectionRange));
    }

    [Fact]
    public void GetDefinition_LocalVariable_ReturnsFirstLocalAssignment()
    {
        const string source = """
            function Run():
                value = 10;
                Print(value);
            endfunction
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 3, 11);

        Assert.NotNull(definition);
        Assert.Equal(("value", RuleScriptSymbolKind.Variable), (definition.Name, definition.Kind));
        Assert.Equal((2, 5, 2, 10), RangeTuple(definition.SelectionRange));
    }

    [Fact]
    public void GetDefinition_GlobalVariable_ReturnsFirstGlobalAssignment()
    {
        const string source = """
            counter = 0;

            Print(counter);
            """;

        var definition = RuleScriptLanguageService.GetDefinition(source, 3, 7);

        Assert.NotNull(definition);
        Assert.Equal(("counter", RuleScriptSymbolKind.Variable), (definition.Name, definition.Kind));
        Assert.Equal((1, 1, 1, 8), RangeTuple(definition.SelectionRange));
    }

    [Fact]
    public void FindReferences_Function_ReturnsDeclarationAndCalls()
    {
        const string source = """
            function Test():
            endfunction

            Test();
            Test();
            Test();
            """;

        var references = RuleScriptLanguageService.FindReferences(source, 4, 1);

        Assert.Equal(4, references.Count);
        Assert.Single(references.Where(reference => reference.IsDeclaration));
        Assert.All(references, reference => Assert.Equal("Test", reference.Name));
    }

    [Fact]
    public void FindReferences_ImportedFunction_ReturnsImportModuleAndCurrentFileReferences()
    {
        using var project = new RuleScriptProject();
        project.Write("Player.rules", """
            export function UpdatePlayer():
                UpdatePlayer();
            endfunction
            """);
        var engine = new RuleScriptEngine { WorkingDirectory = project.Directory };
        const string source = """
            import "Player";

            UpdatePlayer();
            """;

        var references = RuleScriptLanguageService.FindReferences(engine, source, 3, 1);

        Assert.Equal(3, references.Count);
        Assert.Contains(references, reference => reference.IsDeclaration && reference.Range?.File?.EndsWith("Player.rules") == true);
        Assert.Contains(references, reference => !reference.IsDeclaration && reference.Range?.File?.EndsWith("Player.rules") == true);
        Assert.Contains(references, reference => !reference.IsDeclaration && reference.Range?.File is null);
    }

    [Fact]
    public void FindReferences_HostFunction_ReturnsExternalDeclarationAndCalls()
    {
        const string source = """
            Print("a");
            Print("b");
            Print("c");
            """;

        var references = RuleScriptLanguageService.FindReferences(source, 2, 1);

        Assert.Equal(4, references.Count);
        Assert.Contains(references, reference => reference is { IsDeclaration: true, IsExternal: true, Range: null });
        Assert.Equal(3, references.Count(reference => !reference.IsDeclaration));
    }

    [Fact]
    public void FindReferences_FunctionParameter_ReturnsDeclarationAndUses()
    {
        const string source = """
            function Add(a, b):
                result = a + b;
                Print(a);
            endfunction
            """;

        var references = RuleScriptLanguageService.FindReferences(source, 2, 14);

        Assert.Equal(3, references.Count);
        Assert.Single(references.Where(reference => reference.IsDeclaration));
        Assert.All(references, reference => Assert.Equal("a", reference.Name));
    }

    [Fact]
    public void FindReferences_LocalVariable_ReturnsDeclarationAssignmentTargetsAndUses()
    {
        const string source = """
            function Run():
                value = 10;
                Print(value);
                value = value + 1;
            endfunction
            """;

        var references = RuleScriptLanguageService.FindReferences(source, 3, 11);

        Assert.Equal(4, references.Count);
        Assert.Single(references.Where(reference => reference.IsDeclaration));
        Assert.All(references, reference => Assert.Equal("value", reference.Name));
    }

    [Fact]
    public void FindReferences_GlobalVariable_ReturnsDeclarationAssignmentTargetsAndUses()
    {
        const string source = """
            counter = 0;
            counter = counter + 1;
            Print(counter);
            """;

        var references = RuleScriptLanguageService.FindReferences(source, 3, 7);

        Assert.Equal(4, references.Count);
        Assert.Single(references.Where(reference => reference.IsDeclaration));
        Assert.All(references, reference => Assert.Equal("counter", reference.Name));
    }

    [Fact]
    public void Version170Navigation_DoesNotRegressVersion160EditorApisOrAnalysis()
    {
        const string source = """
            #region Main
            /// Adds one.
            function AddOne(value: number):
                return value + 1;
            endfunction
            #endregion
            """;

        Assert.Single(RuleScriptLanguageService.GetRegions(source));
        Assert.Equal("Adds one.", RuleScriptLanguageService.GetFunctionDocumentation(source, "AddOne"));
        Assert.Contains("function AddOne", RuleScriptFormatter.Format(source));

        var analysis = new RuleScriptEngine().Analyze(source);
        Assert.Contains(analysis.UserFunctions, function => function.Name == "AddOne");
    }

    private static (int StartLine, int StartColumn, int EndLine, int EndColumn) RangeTuple(RuleScriptSourceRange? range)
    {
        Assert.NotNull(range);
        return (range.StartLine, range.StartColumn, range.EndLine, range.EndColumn);
    }

    private sealed class RuleScriptProject : IDisposable
    {
        public RuleScriptProject()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"rulescript-navigation-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public void Write(string fileName, string content)
        {
            File.WriteAllText(Path.Combine(Directory, fileName), content);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
