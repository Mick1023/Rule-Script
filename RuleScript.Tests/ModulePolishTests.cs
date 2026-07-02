using RuleScript.Core.Diagnostics;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ModulePolishTests
{
    [Fact]
    public void ExecuteFile_MissingFileDiagnostic_IncludesExecuteFileOriginalAndResolvedPath()
    {
        using var project = new RuleScriptProject();
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.ExecuteFile("missing.rules"));

        Assert.Contains("ExecuteFile", exception.Message);
        Assert.Contains("missing.rules", exception.Message);
        Assert.Contains(project.PathFor("missing.rules"), exception.Message);
    }

    [Fact]
    public void ImportMissingFileDiagnostic_IncludesImportOriginalImporterAndResolvedPath()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """import "missing.rules";""");

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("Import", exception.Message);
        Assert.Contains("missing.rules", exception.Message);
        Assert.Contains(project.PathFor("main.rules"), exception.Message);
        Assert.Contains(project.PathFor("missing.rules"), exception.Message);
    }

    [Fact]
    public void InvalidImportAlias_ThrowsSyntaxException()
    {
        var exception = Assert.Throws<SyntaxException>(() =>
        {
            var tokens = new RuleScript.Core.Lexer.Lexer("""import "robot.rules" as 123;""").Tokenize();
            _ = new Parser(tokens).Parse();
        });

        Assert.Contains("alias", exception.Message);
        Assert.Contains("identifier", exception.Message);
    }

    [Fact]
    public void DuplicateAliasDiagnostic_IncludesAliasAndFilePath()
    {
        using var project = new RuleScriptProject();
        project.Write("a.rules", "function A(): return 1; endfunction");
        project.Write("b.rules", "function B(): return 2; endfunction");
        project.Write("main.rules", """
            import "a.rules" as robot;
            import "b.rules" as robot;
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("duplicate import alias", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("robot", exception.Message);
        Assert.Contains(project.PathFor("main.rules"), exception.Message);
    }

    [Fact]
    public void UnknownAliasDiagnostic_IncludesAlias()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "result = robot.GetSensor();");

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("unknown alias", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("robot", exception.Message);
    }

    [Fact]
    public void MissingModuleFunctionDiagnostic_IncludesAliasFunctionAndNotFound()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", "function GetSensor(): return \"robot\"; endfunction");
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = robot.Missing();
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("robot", exception.Message);
        Assert.Contains("Missing", exception.Message);
        Assert.Contains("function not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportedFile_WithExecutableStatement_ThrowsRuntimeException()
    {
        using var project = new RuleScriptProject();
        project.Write("bad.rules", "var a = 1;");
        project.Write("main.rules", """import "bad.rules";""");

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("imported file", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("top-level executable statements are not allowed", exception.Message);
        Assert.Contains(project.PathFor("bad.rules"), exception.Message);
    }

    [Fact]
    public void CircularImportDiagnostic_IncludesImportChain()
    {
        using var project = new RuleScriptProject();
        project.Write("A.rules", """import "B.rules";""");
        project.Write("B.rules", """import "A.rules";""");

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "A.rules"));

        Assert.Contains("circular import", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A.rules", exception.Message);
        Assert.Contains("B.rules", exception.Message);
    }

    [Fact]
    public void Import_WithCurrentDirectoryRelativePath_Works()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", "export function Value(): return 7; endfunction");
        project.Write("main.rules", """
            import "./common.rules";

            result = Value();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal(7d, context.Get<double>("result"));
    }

    [Fact]
    public void Import_WithNormalizedParentSegment_Works()
    {
        using var project = new RuleScriptProject();
        Directory.CreateDirectory(project.PathFor("modules"));
        project.Write("common.rules", "export function Value(): return 8; endfunction");
        project.Write("main.rules", """
            import "modules/../common.rules";

            result = Value();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal(8d, context.Get<double>("result"));
    }

    [Fact]
    public void DuplicateImport_ThroughDifferentRelativePaths_Works()
    {
        using var project = new RuleScriptProject();
        Directory.CreateDirectory(project.PathFor("modules"));
        project.Write("common.rules", "export function Value(): return 9; endfunction");
        project.Write("main.rules", """
            import "./common.rules";
            import "modules/../common.rules";

            result = Value();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal(9d, context.Get<double>("result"));
    }

    [Fact]
    public void WorkingDirectory_Null_UsesEnvironmentCurrentDirectory()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "result = 123;");
        var previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = project.DirectoryPath;
            var engine = new RuleScriptEngine
            {
                WorkingDirectory = null
            };

            var context = engine.ExecuteFile("main.rules");

            Assert.Equal(123d, context.Get<double>("result"));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void RelativeExecuteFile_UsesWorkingDirectory()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "result = 456;");
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = project.DirectoryPath
        };

        var context = engine.ExecuteFile("main.rules");

        Assert.Equal(456d, context.Get<double>("result"));
    }

    [Fact]
    public void AbsoluteExecuteFile_IgnoresWorkingDirectory()
    {
        using var project = new RuleScriptProject();
        using var otherProject = new RuleScriptProject();
        project.Write("main.rules", "result = 789;");
        otherProject.Write("main.rules", "result = 0;");
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = otherProject.DirectoryPath
        };

        var context = engine.ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal(789d, context.Get<double>("result"));
    }

    private static RuntimeContext ExecuteFile(RuleScriptProject project, string fileName)
    {
        return new RuleScriptEngine().ExecuteFile(project.PathFor(fileName));
    }

    private sealed class RuleScriptProject : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"rulescript-{Guid.NewGuid():N}");

        public RuleScriptProject()
        {
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Write(string fileName, string content)
        {
            var path = PathFor(fileName);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(DirectoryPath, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
