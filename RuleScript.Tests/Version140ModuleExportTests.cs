using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140ModuleExportTests
{
    [Fact]
    public void Lexer_RecognizesExportAndConstKeywords()
    {
        var tokens = new Lexer("export const A = 1;").Tokenize();

        Assert.Equal(TokenType.Export, tokens[0].Type);
        Assert.Equal(TokenType.Const, tokens[1].Type);
    }

    [Fact]
    public void Parser_MarksExportedDeclarations()
    {
        var statements = Parse("""
            export const A = 1;
            export function Foo(): return A; endfunction
            function Hidden(): return 0; endfunction
            """);

        Assert.True(Assert.IsType<ConstStatement>(statements[0]).IsExported);
        Assert.True(Assert.IsType<FunctionDeclarationStatement>(statements[1]).IsExported);
        Assert.False(Assert.IsType<FunctionDeclarationStatement>(statements[2]).IsExported);
    }

    [Fact]
    public void Analyze_ConstExposesReadonlyExportMetadataAndType()
    {
        var result = new RuleScriptEngine().Analyze("export const Answer = 42;");
        var constant = Assert.Single(result.Variables, variable => variable.Name == "Answer");

        Assert.Equal(RuleScriptValueType.Number, constant.Type);
        Assert.True(constant.IsReadOnly);
        Assert.True(constant.IsExported);
    }

    [Fact]
    public void Analyze_AssigningConstProducesReadonlyDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("const Answer = 42; Answer = 43;");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.CannotAssignToReadonly);
    }

    [Fact]
    public void Execute_AssigningConstFailsAtRuntime()
    {
        Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute("const Answer = 42; Answer = 43;"));
    }

    [Fact]
    public void AliasImport_OnlyExposesExplicitlyExportedFunction()
    {
        using var project = new RuleScriptProject();
        project.Write("library.rules", """
            export function Public(): return Private(); endfunction
            function Private(): return "ok"; endfunction
            """);
        project.Write("main.rules", "import \"library.rules\" as lib; result = lib.Public();");

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal("ok", context.Get<string>("result"));
    }

    [Fact]
    public void AliasImport_PrivateFunctionIsNotAccessible()
    {
        using var project = new RuleScriptProject();
        project.Write("library.rules", """
            export function Public(): return "public"; endfunction
            function Private(): return "private"; endfunction
            """);
        project.Write("main.rules", "import \"library.rules\" as lib; result = lib.Private();");

        Assert.Throws<RuntimeException>(() => new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules")));
    }

    [Fact]
    public void GlobalImport_OnlyAddsExportedFunction()
    {
        using var project = new RuleScriptProject();
        project.Write("library.rules", """
            export function Public(): return "public"; endfunction
            function Private(): return "private"; endfunction
            """);
        project.Write("main.rules", "import \"library.rules\"; result = Public();");

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));
        Assert.Equal("public", context.Get<string>("result"));
    }

    [Fact]
    public void ModuleWithoutExportsRetainsLegacyImplicitVisibility()
    {
        using var project = new RuleScriptProject();
        project.Write("legacy.rules", "function Legacy(): return \"legacy\"; endfunction");
        project.Write("main.rules", "import \"legacy.rules\" as old; result = old.Legacy();");

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));
        Assert.Equal("legacy", context.Get<string>("result"));
    }

    [Fact]
    public void AliasAndGlobalImportsCanReadExportedConstants()
    {
        using var project = new RuleScriptProject();
        project.Write("values.rules", "export const Answer = 42;");
        project.Write("main.rules", """
            import "values.rules" as values;
            import "values.rules";
            result = values.Answer + Answer;
            """);

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));
        Assert.Equal(84d, context.Get<double>("result"));
    }

    [Fact]
    public void PrivateConstantIsNotAccessibleThroughAlias()
    {
        using var project = new RuleScriptProject();
        project.Write("values.rules", "export const Public = 1; const Private = 2;");
        project.Write("main.rules", "import \"values.rules\" as values; result = values.Private;");

        Assert.Throws<RuntimeException>(() => new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules")));
    }

    [Fact]
    public void ExportedConstantIsEvaluatedOncePerExecution()
    {
        using var project = new RuleScriptProject();
        project.Write("values.rules", "export const Value = Next();");
        project.Write("main.rules", "import \"values.rules\" as values; result = values.Value + values.Value;");
        var calls = 0;
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Next", _ => ++calls);

        var context = engine.ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal(2d, context.Get<double>("result"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Analyze_ImportCompletionAndSignaturesOnlyIncludeExports()
    {
        using var project = new RuleScriptProject();
        project.Write("library.rules", """
            export function Public(): return 1; endfunction
            function Private(): return 2; endfunction
            export const Answer = 42;
            const Secret = 0;
            """);
        var engine = new RuleScriptEngine { WorkingDirectory = project.DirectoryPath };

        var result = engine.Analyze("""
            import "library.rules" as lib;
            var answer = lib.Answer;
            """);

        Assert.Contains(result.UserFunctions, function => function.Name == "lib.Public");
        Assert.DoesNotContain(result.UserFunctions, function => function.Name == "lib.Private");
        Assert.Equal(RuleScriptValueType.Number, Assert.Single(result.Variables, variable => variable.Name == "answer").Type);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedVariable);
    }

    [Fact]
    public void Analyze_PrivateImportedFunctionProducesUndefinedFunctionDiagnostic()
    {
        using var project = new RuleScriptProject();
        project.Write("library.rules", """
            export function Public(): return 1; endfunction
            function Private(): return 2; endfunction
            """);
        var engine = new RuleScriptEngine { WorkingDirectory = project.DirectoryPath };

        var result = engine.TryAnalyze("import \"library.rules\" as lib; var value = lib.Private();");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private sealed class RuleScriptProject : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rulescript-export-{Guid.NewGuid():N}");

        public string DirectoryPath => _directory;

        public RuleScriptProject()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Write(string fileName, string content)
        {
            File.WriteAllText(PathFor(fileName), content);
        }

        public string PathFor(string fileName) => Path.Combine(_directory, fileName);

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
