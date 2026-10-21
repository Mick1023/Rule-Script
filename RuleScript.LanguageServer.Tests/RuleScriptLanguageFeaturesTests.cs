using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RuleScript.Core;
using RuleScript.LanguageServer;

namespace RuleScript.LanguageServer.Tests;

public sealed class RuleScriptLanguageFeaturesTests
{
    private static readonly DocumentUri Uri = DocumentUri.From("untitled:main.rules");

    private const string Source = """
        /// Adds one.
        function AddOne(value: number) -> number:
            return value + 1;
        endfunction

        var total = AddOne(1);
        """;

    [Fact]
    public void GetDiagnostics_ConvertsToZeroBasedRangesWithCodeAndSeverity()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var a = 1;\nvar broken = ;");

        var diagnostic = Assert.Single(RuleScriptLanguageFeatures.GetDiagnostics(document, filePath: null));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Range.Start.Line);
        Assert.Equal(13, diagnostic.Range.Start.Character);
        Assert.Equal("rulescript", diagnostic.Source);
        Assert.Equal("Expected expression. Unexpected token ';'.", diagnostic.Message);
        Assert.NotNull(diagnostic.Code);
    }

    [Fact]
    public void GetDefinition_MapsCallToDeclarationInSameDocument()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var location = Assert.Single(RuleScriptLanguageFeatures.GetDefinition(document, Uri, new Position(5, 13)));

        Assert.Equal(Uri, location.Uri);
        Assert.Equal(new Position(1, 9), location.Range.Start);
        Assert.Equal(new Position(1, 15), location.Range.End);
    }

    [Fact]
    public void GetDefinition_ReturnsNothingForBuiltins()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var n = Length(\"abc\");");

        Assert.Empty(RuleScriptLanguageFeatures.GetDefinition(document, Uri, new Position(0, 9)));
    }

    [Fact]
    public void GetReferences_HonorsIncludeDeclaration()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        Assert.Equal(2, RuleScriptLanguageFeatures.GetReferences(document, Uri, new Position(1, 10), includeDeclaration: true).Count);
        Assert.Single(RuleScriptLanguageFeatures.GetReferences(document, Uri, new Position(1, 10), includeDeclaration: false));
    }

    [Fact]
    public void GetHover_ReturnsMarkdownWithSignatureAndDocumentation()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var hover = RuleScriptLanguageFeatures.GetHover(document, new Position(5, 13));

        Assert.NotNull(hover);
        Assert.Equal(
            "```rulescript\nfunction AddOne(value: number) -> number\n```\n\nAdds one.",
            hover.Contents.MarkupContent!.Value);
        Assert.Equal(new Position(5, 12), hover.Range!.Start);
    }

    [Fact]
    public void GetFoldingRanges_KeepsClosingKeywordVisible()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var range = Assert.Single(RuleScriptLanguageFeatures.GetFoldingRanges(document));

        Assert.Equal(1, range.StartLine);
        Assert.Equal(2, range.EndLine);
    }

    [Fact]
    public void GetDocumentSymbols_MapsKindsAndChildren()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var symbols = RuleScriptLanguageFeatures.GetDocumentSymbols(document);

        Assert.Equal(["AddOne", "total"], symbols.Select(symbol => symbol.Name));
        Assert.Equal(SymbolKind.Function, symbols[0].Kind);
        Assert.Equal("(value: number) -> number", symbols[0].Detail);
        Assert.Equal(SymbolKind.Variable, symbols[1].Kind);
    }

    [Fact]
    public void GetCompletions_MapsKindsTextEditsAndSnippetFormat()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source + "\nvar next = to");

        var items = RuleScriptLanguageFeatures.GetCompletions(document, new Position(6, 13), snippetSupport: true).Items.ToArray();

        var total = Assert.Single(items, item => item.Label == "total");
        Assert.Equal(CompletionItemKind.Variable, total.Kind);
        Assert.Equal(new Position(6, 11), total.TextEdit!.TextEdit!.Range.Start);
        Assert.Equal(new Position(6, 13), total.TextEdit.TextEdit.Range.End);

        var addOne = Assert.Single(items, item => item.Label == "AddOne");
        Assert.Equal(CompletionItemKind.Function, addOne.Kind);
        Assert.Equal("Adds one.", addOne.Documentation!.MarkupContent!.Value);
        Assert.True(string.CompareOrdinal(total.SortText, addOne.SortText) < 0);

        var snippet = Assert.Single(items, item => item.Label == "foreach" && item.Kind == CompletionItemKind.Snippet);
        Assert.Equal(InsertTextFormat.Snippet, snippet.InsertTextFormat);
    }

    [Fact]
    public void GetCompletions_OmitsSnippetsWhenClientCannotExpandThem()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var items = RuleScriptLanguageFeatures.GetCompletions(document, new Position(0, 0), snippetSupport: false).Items;

        Assert.DoesNotContain(items, item => item.InsertTextFormat == InsertTextFormat.Snippet);
        Assert.Contains(items, item => item.Label == "foreach" && item.Kind == CompletionItemKind.Keyword);
    }

    [Fact]
    public void GetSignatureHelp_MapsParameterOffsets()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source + "\nvar next = AddOne(");

        var help = RuleScriptLanguageFeatures.GetSignatureHelp(document, new Position(6, 18));

        Assert.NotNull(help);
        var signature = Assert.Single(help.Signatures);
        Assert.Equal("AddOne(value: number) -> number", signature.Label);
        var parameter = Assert.Single(signature.Parameters!);
        Assert.Equal((7, 20), parameter.Label.Range);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public void Format_ReplacesWholeDocumentOnlyWhenChanged()
    {
        var edit = Assert.Single(RuleScriptLanguageFeatures.Format("var   a=1;"));
        Assert.Equal("var a = 1;\n", edit.NewText);
        Assert.Equal(new Position(0, 0), edit.Range.Start);

        Assert.Empty(RuleScriptLanguageFeatures.Format(edit.NewText));
    }

    [Fact]
    public void Format_SkipsDocumentsWithSyntaxErrors()
    {
        Assert.Empty(RuleScriptLanguageFeatures.Format("var a = \"open"));
    }
}
