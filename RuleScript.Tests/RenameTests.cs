using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RenameTests
{
    private const string Source = """
        var count = 1;
        function Increase(step: number) -> number:
            return count + step;
        endfunction
        count = Increase(count);
        foreach item in [1, 2]:
            Print(item);
        endforeach
        """;

    [Fact]
    public void Rename_Variable_EditsEveryOccurrence()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var result = RuleScriptLanguageService.Rename(document, 1, 5, "total");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(
            [
                new RuleScriptSourceRange(null, 1, 5, 1, 10),
                new RuleScriptSourceRange(null, 3, 12, 3, 17),
                new RuleScriptSourceRange(null, 5, 1, 5, 6),
                new RuleScriptSourceRange(null, 5, 18, 5, 23)
            ],
            result.Edits);
    }

    [Fact]
    public void Rename_FunctionParameterAndLoopVariable()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var function = RuleScriptLanguageService.Rename(document, 5, 9, "Grow");
        var parameter = RuleScriptLanguageService.Rename(document, 3, 20, "amount");
        var loopVariable = RuleScriptLanguageService.Rename(document, 7, 11, "entry");

        Assert.Equal(2, function.Edits.Count);
        Assert.Equal(2, parameter.Edits.Count);
        Assert.All(parameter.Edits, edit => Assert.InRange(edit.StartLine, 2, 3));
        Assert.Equal(
            [new RuleScriptSourceRange(null, 6, 9, 6, 13), new RuleScriptSourceRange(null, 7, 11, 7, 15)],
            loopVariable.Edits);
    }

    [Fact]
    public void Rename_SameName_SucceedsWithoutEdits()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var result = RuleScriptLanguageService.Rename(document, 1, 5, "count");

        Assert.True(result.Success);
        Assert.Empty(result.Edits);
    }

    [Theory]
    [InlineData("1count")]
    [InlineData("my-count")]
    [InlineData("if")]
    [InlineData("endforeach")]
    [InlineData("global")]
    [InlineData("")]
    public void Rename_RejectsInvalidNames(string newName)
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var result = RuleScriptLanguageService.Rename(document, 1, 5, newName);

        Assert.False(result.Success);
        Assert.Contains("not a valid", result.ErrorMessage);
        Assert.Empty(result.Edits);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("Increase")]
    [InlineData("step")]
    [InlineData("Length")]
    public void Rename_RejectsNamesAlreadyInUse(string newName)
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var result = RuleScriptLanguageService.Rename(document, 1, 5, newName);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public void Rename_RejectsBuiltinAndHostFunctions()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("HostScore", _ => 1, new RuleScriptHostFunctionOptions());
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, "var a = Length(\"x\");\nvar b = HostScore();");

        var builtin = RuleScriptLanguageService.Rename(document, 1, 9, "Size");
        var host = RuleScriptLanguageService.Rename(document, 2, 9, "Score");

        Assert.False(builtin.Success);
        Assert.False(host.Success);
        Assert.Contains("Host Functions", builtin.ErrorMessage);
    }

    [Fact]
    public void Rename_RejectsExportedSymbols()
    {
        const string source = """
            export function Shared():
                return 1;
            endfunction
            export const LIMIT = 2;
            var value = Shared() + LIMIT;
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.False(RuleScriptLanguageService.Rename(document, 5, 13, "Common").Success);
        Assert.False(RuleScriptLanguageService.Rename(document, 5, 24, "MAX").Success);
        Assert.True(RuleScriptLanguageService.Rename(document, 5, 5, "result").Success);
    }

    [Fact]
    public void Rename_RejectsSymbolsDeclaredInImportedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rulescript-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "lib.rules"), "export function Helper():\n    return 1;\nendfunction");
            var engine = new RuleScriptEngine { WorkingDirectory = root };
            var document = RuleScriptLanguageService.TryAnalyzeDocument(
                engine,
                "import \"lib\" as lib;\nvar value = lib.Helper();",
                Path.Combine(root, "main.rules"));

            var result = RuleScriptLanguageService.Rename(document, 2, 17, "Assist");

            Assert.False(result.Success);
            Assert.Contains("imported files", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrepareRename_ReturnsNameAndRangeOrReason()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var variable = RuleScriptLanguageService.PrepareRename(document, 3, 14);
        var whitespace = RuleScriptLanguageService.PrepareRename(document, 4, 12);
        var builtin = RuleScriptLanguageService.PrepareRename(document, 7, 6);

        Assert.True(variable.CanRename);
        Assert.Equal("count", variable.Name);
        Assert.Equal(new RuleScriptSourceRange(null, 3, 12, 3, 17), variable.Range);
        Assert.False(whitespace.CanRename);
        Assert.False(builtin.CanRename);
        Assert.NotNull(builtin.RejectReason);
    }
}
