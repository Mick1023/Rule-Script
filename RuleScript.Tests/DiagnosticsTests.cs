using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Lexer_UnterminatedString_IncludesLineAndColumn()
    {
        var exception = Assert.Throws<SyntaxException>(() => new Lexer("var name = \"Mick;").Tokenize());

        Assert.Equal(1, exception.Line);
        Assert.Equal(12, exception.Column);
        Assert.Contains("Line 1, Column 12", exception.Message);
        Assert.Contains("Unterminated string literal", exception.Message);
    }

    [Fact]
    public void Lexer_UnsupportedCharacter_IncludesLineAndColumn()
    {
        var exception = Assert.Throws<SyntaxException>(() => new Lexer("var a = @;").Tokenize());

        Assert.Equal(1, exception.Line);
        Assert.Equal(9, exception.Column);
        Assert.Equal("@", exception.TokenText);
        Assert.Contains("Line 1, Column 9", exception.Message);
        Assert.Contains("@", exception.Message);
    }

    [Fact]
    public void Parser_MissingSemicolon_IncludesLineAndColumn()
    {
        var exception = Assert.Throws<SyntaxException>(() => Execute("var a = 1"));

        Assert.Equal(1, exception.Line);
        Assert.Contains("Line 1, Column", exception.Message);
        Assert.Contains("Expected ';' after variable declaration", exception.Message);
    }

    [Fact]
    public void Parser_MissingEndIf_IncludesLineAndColumn()
    {
        var exception = Assert.Throws<SyntaxException>(() => Execute("""
            if true then:
                result = "OK";
            """));

        Assert.NotNull(exception.Line);
        Assert.NotNull(exception.Column);
        Assert.Contains("Expected 'endif'", exception.Message);
    }

    [Fact]
    public void Parser_UnexpectedToken_IncludesTokenText()
    {
        var exception = Assert.Throws<SyntaxException>(() => Execute("var a = ;"));

        Assert.Equal(";", exception.TokenText);
        Assert.Contains("Unexpected token ';'", exception.Message);
    }

    [Fact]
    public void ExecuteFile_SyntaxErrorInImportedFile_MessageIncludesImportedFile()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", """import "base.rules";""");
        project.Write("base.rules", """
            function Add(a, b)
                return a + b;
            endfunction
            """);
        var engine = new RuleScriptEngine();

        var exception = Assert.Throws<SyntaxException>(() => { engine.ExecuteFile(project.PathFor("main.rules")); });

        Assert.Equal(project.PathFor("base.rules"), exception.SourceFile);
        Assert.Contains(project.PathFor("base.rules"), exception.Message);
        Assert.Contains("Line 2, Column", exception.Message);
        Assert.Contains("Expected ':' after function declaration", exception.Message);
    }

    [Fact]
    public void ExecuteFile_SyntaxErrorInMainFile_MessageIncludesFile()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "var value = ;");
        var engine = new RuleScriptEngine();

        var exception = Assert.Throws<SyntaxException>(() => { engine.ExecuteFile(project.PathFor("main.rules")); });

        Assert.Equal(project.PathFor("main.rules"), exception.SourceFile);
        Assert.Contains(project.PathFor("main.rules"), exception.Message);
        Assert.Contains("Line 1, Column", exception.Message);
    }

    [Fact]
    public void Runtime_UndefinedVariable_IncludesVariableName()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("result = distance;"));

        Assert.Equal("distance", exception.TokenText);
        Assert.Contains("Undefined variable 'distance'", exception.Message);
    }

    [Fact]
    public void Runtime_UnknownFunction_IncludesFunctionName()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("Missing();"));

        Assert.Equal("Missing", exception.TokenText);
        Assert.Contains("Function 'Missing' is not registered", exception.Message);
    }

    [Fact]
    public void Runtime_WrongArgumentCount_IncludesFunctionName()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("ToString(1, 2);"));

        Assert.Equal("ToString", exception.TokenText);
        Assert.Contains("ToString", exception.Message);
        Assert.Contains("expects 1 argument", exception.Message);
    }

    [Fact]
    public void Runtime_InvalidTypeOperation_IncludesOperator()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("result = true - 1;"));

        Assert.Equal("-", exception.TokenText);
        Assert.Contains("Operator '-'", exception.Message);
    }

    [Fact]
    public void Runtime_HostFunctionException_IncludesFunctionName()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Fail", _ => throw new InvalidOperationException("boom"));

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("Fail();", new RuntimeContext()));

        Assert.Equal("Fail", exception.TokenText);
        Assert.Contains("Fail", exception.Message);
    }

    private static void Execute(string script)
    {
        var engine = new RuleScriptEngine();
        var context = new RuntimeContext();

        engine.Execute(script, context);
    }

    private sealed class RuleScriptProject : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rulescript-tests", Guid.NewGuid().ToString("N"));

        public RuleScriptProject()
        {
            Directory.CreateDirectory(_root);
        }

        public void Write(string fileName, string content)
        {
            var path = PathFor(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(_root, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
