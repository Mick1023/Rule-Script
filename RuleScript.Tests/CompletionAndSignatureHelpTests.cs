using System.Text.RegularExpressions;
using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class CompletionAndSignatureHelpTests
{
    private const string FunctionSource = """
        var outer = 1;
        /// Doubles the rate.
        function Calc(rate: number) -> number:
            var local = rate * 2;
            return local;
        endfunction
        """;

    [Fact]
    public void ForeachVariable_NavigatesToVariableName()
    {
        const string source = """
            var items = [1, 2];
            foreach item in items:
                Print(item);
            endforeach
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var definition = RuleScriptLanguageService.GetDefinition(document, 3, 11);

        Assert.NotNull(definition);
        Assert.Equal("item", definition.Name);
        Assert.Equal(2, definition.Line);
        Assert.Equal(9, definition.Column);
        Assert.Equal(2, RuleScriptLanguageService.FindReferences(document, 2, 9).Count);
    }

    [Fact]
    public void ForeachTypeMismatch_StillPointsAtForeachKeyword()
    {
        var attempt = new RuleScriptEngine().TryAnalyze("foreach item in 5:\n    Print(item);\nendforeach");

        var diagnostic = Assert.Single(attempt.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void GetCompletions_InsideFunction_IncludesScopeFunctionsKeywordsAndSnippets()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(FunctionSource);

        var completions = RuleScriptLanguageService.GetCompletions(document, 5, 14);

        Assert.Equal(new RuleScriptSourceRange(null, 5, 12, 5, 14), completions.ReplaceRange);
        var rate = Assert.Single(completions.Items, item => item.Label == "rate");
        Assert.Equal(RuleScriptCompletionItemKind.Parameter, rate.Kind);
        Assert.Equal("parameter: number", rate.Detail);
        Assert.Contains(completions.Items, item => item.Label == "local" && item.Kind == RuleScriptCompletionItemKind.Variable);

        var calc = Assert.Single(completions.Items, item => item.Label == "Calc");
        Assert.Equal(RuleScriptCompletionItemKind.Function, calc.Kind);
        Assert.Equal("function Calc(rate: number) -> number", calc.Detail);
        Assert.Equal("Doubles the rate.", calc.Documentation);

        Assert.Contains(completions.Items, item => item.Label == "Substring" && item.Kind == RuleScriptCompletionItemKind.BuiltinFunction);
        Assert.Contains(completions.Items, item => item.Label == "endfunction" && item.Kind == RuleScriptCompletionItemKind.Keyword);
        Assert.Contains(completions.Items, item => item.Label == "foreach" && item.Kind == RuleScriptCompletionItemKind.Snippet && item.IsSnippet);
    }

    [Fact]
    public void GetCompletions_IncompleteLine_StillOffersDeclaredVariables()
    {
        const string source = "var total = 1;\nvar next = to";
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var completions = RuleScriptLanguageService.GetCompletions(document, 2, EndColumn(source, 2));

        Assert.Contains(completions.Items, item => item.Label == "total");
        Assert.Equal(12, completions.ReplaceRange.StartColumn);
    }

    [Fact]
    public void GetCompletions_IncludesHostFunctions()
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
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, "var score = ");

        var host = Assert.Single(RuleScriptLanguageService.GetCompletions(document, 1, 13).Items, item => item.Label == "HostScore");

        Assert.Equal(RuleScriptCompletionItemKind.HostFunction, host.Kind);
        Assert.Equal("host function HostScore(playerId: string) -> number", host.Detail);
    }

    [Theory]
    [InlineData("var text = \"abc")]
    [InlineData("var text = \"a b\" // comm")]
    [InlineData("/* open")]
    [InlineData("var ")]
    [InlineData("function ")]
    [InlineData("var value = payload.")]
    public void GetCompletions_ReturnsNothingInStringsCommentsDeclarationsAndUnknownMembers(string source)
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.Empty(RuleScriptLanguageService.GetCompletions(document, 1, source.Length + 1).Items);
    }

    [Fact]
    public void GetCompletions_AfterAt_OffersHostTriggerAttribute()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("@");

        var item = Assert.Single(RuleScriptLanguageService.GetCompletions(document, 1, 2).Items);

        Assert.Equal("HostTrigger", item.Label);
        Assert.True(item.IsSnippet);
    }

    [Fact]
    public void Snippets_ProduceParseableCode()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("");
        var snippets = RuleScriptLanguageService.GetCompletions(document, 1, 1).Items
            .Where(item => item.Kind == RuleScriptCompletionItemKind.Snippet)
            .ToArray();

        Assert.NotEmpty(snippets);
        foreach (var snippet in snippets)
        {
            var code = Regex.Replace(snippet.InsertText, @"\$\{\d+:([^}]*)\}", "$1");
            code = Regex.Replace(code, @"\$\{\d+\}", string.Empty);
            code = Regex.Replace(code, @"\$\d+", "Print(1);");

            var result = RuleScriptLanguageService.TryAnalyzeDocument(code);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.SyntaxError);
        }
    }

    [Fact]
    public void ImportAlias_CompletesMembersAndSignatureHelp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rulescript-completion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "lib.rules"), """
                /// Combines two values.
                export function Join2(left: string, right: string) -> string:
                    return left + right;
                endfunction
                """);
            var engine = new RuleScriptEngine { WorkingDirectory = root };

            var memberSource = "import \"lib\" as lib;\nvar value = lib.";
            var memberDocument = RuleScriptLanguageService.TryAnalyzeDocument(engine, memberSource, Path.Combine(root, "main.rules"));
            var member = Assert.Single(RuleScriptLanguageService.GetCompletions(memberDocument, 2, EndColumn(memberSource, 2)).Items);
            Assert.Equal("Join2", member.Label);
            Assert.Equal("function Join2(left: string, right: string) -> string", member.Detail);

            var callSource = "import \"lib\" as lib;\nvar value = lib.Join2(\"a\", ";
            var callDocument = RuleScriptLanguageService.TryAnalyzeDocument(engine, callSource, Path.Combine(root, "main.rules"));
            var help = RuleScriptLanguageService.GetSignatureHelp(callDocument, 2, EndColumn(callSource, 2));
            Assert.NotNull(help);
            Assert.Equal("lib.Join2(left: string, right: string) -> string", help.Signatures[0].Label);
            Assert.Equal(1, help.ActiveParameter);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetSignatureHelp_ReportsLabelOffsetsDocumentationAndActiveParameter()
    {
        var source = FunctionSource + "\nvar result = Calc(";
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var help = RuleScriptLanguageService.GetSignatureHelp(document, 7, EndColumn(source, 7));

        Assert.NotNull(help);
        var signature = Assert.Single(help.Signatures);
        Assert.Equal("Calc(rate: number) -> number", signature.Label);
        Assert.Equal("Doubles the rate.", signature.Documentation);
        var parameter = Assert.Single(signature.Parameters);
        Assert.Equal("rate: number", parameter.Label);
        Assert.Equal("rate: number", signature.Label[parameter.Start..parameter.End]);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Theory]
    [InlineData("var s = Substring(\"abc\", ", "Substring", 1)]
    [InlineData("var s = Substring(Length(\"x\"), 0, ", "Substring", 2)]
    [InlineData("var s = Substring(Length(", "Length", 0)]
    [InlineData("var s = Substring([1, 2], ", "Substring", 1)]
    [InlineData("var s = Substring({ a: 1, b: 2 }, ", "Substring", 1)]
    public void GetSignatureHelp_TracksNestedCallsAndLiterals(string source, string expectedFunction, int expectedParameter)
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var help = RuleScriptLanguageService.GetSignatureHelp(document, 1, source.Length + 1);

        Assert.NotNull(help);
        Assert.StartsWith(expectedFunction + "(", help.Signatures[help.ActiveSignature].Label);
        Assert.Equal(expectedParameter, help.ActiveParameter);
    }

    [Theory]
    [InlineData("var s = Length(\"abc\");")]
    [InlineData("var s = 1 + ")]
    [InlineData("var s = Unknown(")]
    public void GetSignatureHelp_ReturnsNullOutsideKnownCalls(string source)
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.Null(RuleScriptLanguageService.GetSignatureHelp(document, 1, source.Length + 1));
    }

    private static int EndColumn(string source, int line)
    {
        return source.Split('\n')[line - 1].Length + 1;
    }
}
