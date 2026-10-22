using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class LanguageServiceEditorFeatureTests
{
    private const string Source = """
        /// Adds one.
        function AddOne(value: number) -> number:
            return value + 1;
        endfunction

        const LIMIT = 10;
        var total = AddOne(LIMIT);
        var text = Substring("abc", 0, 1);
        """;

    [Fact]
    public void GetHover_UserFunction_ReturnsSignatureAndDocumentation()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        var hover = RuleScriptLanguageService.GetHover(document, 7, 13);

        Assert.NotNull(hover);
        Assert.Equal("function AddOne(value: number) -> number", hover.Signature);
        Assert.Equal("Adds one.", hover.Documentation);
        Assert.Equal(new RuleScriptSourceRange(null, 7, 13, 7, 19), hover.Range);
        Assert.False(hover.IsExternal);
    }

    [Fact]
    public void GetHover_ParameterAndConstant_IncludeTypes()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        Assert.Equal("(parameter) value: number", RuleScriptLanguageService.GetHover(document, 3, 12)?.Signature);
        Assert.Equal("const LIMIT: number", RuleScriptLanguageService.GetHover(document, 7, 20)?.Signature);
    }

    [Fact]
    public void GetHover_BuiltinAndHostFunctions_AreMarkedExternal()
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
        var document = RuleScriptLanguageService.TryAnalyzeDocument(engine, Source + "\nvar score = HostScore(\"p1\");");

        var builtin = RuleScriptLanguageService.GetHover(document, 8, 12);
        var host = RuleScriptLanguageService.GetHover(document, 9, 13);

        Assert.NotNull(builtin);
        Assert.Equal("builtin function Substring(value: any, start: number, length: number) -> string", builtin.Signature);
        Assert.True(builtin.IsExternal);
        Assert.NotNull(host);
        Assert.Equal("host function HostScore(playerId: string) -> number", host.Signature);
        Assert.Equal("Reads the player's score.", host.Documentation);
    }

    [Fact]
    public void GetHover_BuiltinOverload_MatchesCallArgumentCount()
    {
        const string source = """
            var two = PadLeft("5", 2);
            var three = PadLeft("5", 2, "0");
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.Equal(
            "builtin function PadLeft(value: any, totalWidth: any) -> string",
            RuleScriptLanguageService.GetHover(document, 1, 11)?.Signature);
        Assert.Equal(
            "builtin function PadLeft(value: any, totalWidth: any, padding: any) -> string",
            RuleScriptLanguageService.GetHover(document, 2, 13)?.Signature);
    }

    [Fact]
    public void GetHover_UserOverload_MatchesCallAndDeclaration()
    {
        const string source = """
            /// Scales by two.
            function Scale(value: number) -> number:
                return value * 2;
            endfunction

            /// Scales by a factor.
            function Scale(value: number, factor: number) -> number:
                return value * factor;
            endfunction

            var one = Scale(1);
            var two = Scale(1, 3);
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var oneArgument = RuleScriptLanguageService.GetHover(document, 11, 11);
        Assert.Equal("function Scale(value: number) -> number", oneArgument?.Signature);
        Assert.Equal("Scales by two.", oneArgument?.Documentation);
        Assert.Equal(
            "function Scale(value: number, factor: number) -> number",
            RuleScriptLanguageService.GetHover(document, 12, 11)?.Signature);
        Assert.Equal(
            "function Scale(value: number) -> number",
            RuleScriptLanguageService.GetHover(document, 2, 10)?.Signature);
    }

    [Fact]
    public void GetDefinition_UserOverload_MatchesCallAndDeclaration()
    {
        const string source = """
            function Scale(value: number) -> number:
                return value * 2;
            endfunction

            function Scale(value: number, factor: number) -> number:
                return value * factor;
            endfunction

            var one = Scale(1);
            var two = Scale(1, 3);
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        Assert.Equal(1, RuleScriptLanguageService.GetDefinition(document, 9, 11)?.SelectionRange?.StartLine);
        Assert.Equal(5, RuleScriptLanguageService.GetDefinition(document, 10, 11)?.SelectionRange?.StartLine);
        Assert.Equal(1, RuleScriptLanguageService.GetDefinition(document, 1, 10)?.SelectionRange?.StartLine);
    }

    [Fact]
    public void GetDefinition_BuiltinOverload_MatchesCallArgumentCount()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var text = PadLeft(\"5\", 2);");

        var definition = RuleScriptLanguageService.GetDefinition(document, 1, 12);

        Assert.NotNull(definition);
        Assert.True(definition.IsExternal);
        Assert.Equal(["value", "totalWidth"], definition.Parameters!.Select(parameter => parameter.Name));
        Assert.Equal("Pads text on the left with spaces to the specified total width.", definition.Documentation);
    }

    private const string TypedOverloadSource = """
        /// Describes a number.
        function Describe(value: number) -> string:
            return "number";
        endfunction

        /// Describes a string.
        function Describe(value: string) -> string:
            return "string";
        endfunction

        var a = Describe(7);
        var b = Describe("ok");
        var c = Describe(8);
        """;

    [Fact]
    public void GetHoverAndDefinition_SameArityOverload_MatchesArgumentTypes()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(TypedOverloadSource);

        var numberHover = RuleScriptLanguageService.GetHover(document, 11, 9);
        var stringHover = RuleScriptLanguageService.GetHover(document, 12, 9);

        Assert.Equal("function Describe(value: number) -> string", numberHover?.Signature);
        Assert.Equal("Describes a number.", numberHover?.Documentation);
        Assert.Equal("function Describe(value: string) -> string", stringHover?.Signature);
        Assert.Equal("Describes a string.", stringHover?.Documentation);
        Assert.Equal(2, RuleScriptLanguageService.GetDefinition(document, 11, 9)?.SelectionRange?.StartLine);
        Assert.Equal(7, RuleScriptLanguageService.GetDefinition(document, 12, 9)?.SelectionRange?.StartLine);
        Assert.Equal("function Describe(value: number) -> string", RuleScriptLanguageService.GetHover(document, 2, 10)?.Signature);
        Assert.Equal("function Describe(value: string) -> string", RuleScriptLanguageService.GetHover(document, 7, 10)?.Signature);
    }

    [Fact]
    public void FindReferences_Overload_ReturnsOnlyThatOverload()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(TypedOverloadSource);

        var fromCall = RuleScriptLanguageService.FindReferences(document, 11, 9);
        var fromDeclaration = RuleScriptLanguageService.FindReferences(document, 7, 10);

        Assert.Equal([2, 11, 13], fromCall.Select(reference => reference.Range!.StartLine));
        Assert.Equal([7, 12], fromDeclaration.Select(reference => reference.Range!.StartLine));
    }

    [Fact]
    public void FindReferences_BuiltinOverload_ReturnsOnlyMatchingArgumentCount()
    {
        const string source = """
            var a = PadLeft("5", 2);
            var b = PadLeft("5", 2, "0");
            var c = PadLeft("6", 3);
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var references = RuleScriptLanguageService.FindReferences(document, 1, 9)
            .Where(reference => reference.Range is not null);

        Assert.Equal([1, 3], references.Select(reference => reference.Range!.StartLine));
    }

    [Fact]
    public void Rename_Overload_EditsOnlyThatOverload()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(TypedOverloadSource);

        var result = RuleScriptLanguageService.Rename(document, 7, 10, "DescribeText");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal([7, 12], result.Edits.Select(edit => edit.StartLine));
    }

    [Fact]
    public void GetHover_ReturnsNullAwayFromSymbols()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument(Source);

        Assert.Null(RuleScriptLanguageService.GetHover(document, 4, 1));
    }

    [Fact]
    public void GetDocumentSymbols_ReturnsHierarchicalOutline()
    {
        const string source = """
            import "robot.rules" as robot;

            @HostTrigger("LotArrived")
            function OnLot(lotId: string) -> void:
                var count = 1;
                if count > 0 then:
                    const MAX = 2;
                endif
                return;
            endfunction

            var total = 1;
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var symbols = RuleScriptLanguageService.GetDocumentSymbols(document);

        Assert.Collection(
            symbols,
            import =>
            {
                Assert.Equal("robot", import.Name);
                Assert.Equal(RuleScriptDocumentSymbolKind.Import, import.Kind);
                Assert.Equal("robot.rules", import.Detail);
            },
            function =>
            {
                Assert.Equal("OnLot", function.Name);
                Assert.Equal(RuleScriptDocumentSymbolKind.Function, function.Kind);
                Assert.Equal("@HostTrigger(\"LotArrived\") (lotId: string) -> void", function.Detail);
                Assert.Equal(4, function.Range.StartLine);
                Assert.Equal(10, function.Range.EndLine);
                Assert.Equal(new RuleScriptSourceRange(null, 4, 10, 4, 15), function.SelectionRange);
                Assert.Equal(["count", "MAX"], function.Children.Select(child => child.Name));
                Assert.Equal(RuleScriptDocumentSymbolKind.Constant, function.Children[1].Kind);
            },
            variable => Assert.Equal("total", variable.Name));
    }

    [Fact]
    public void GetDocumentSymbols_KeepsValidDeclarationsInIncompleteDocument()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var a = 1;\nvar broken = ;\nvar c = 3;");

        var names = RuleScriptLanguageService.GetDocumentSymbols(document).Select(symbol => symbol.Name);

        Assert.Equal(["a", "c"], names);
    }

    [Fact]
    public void GetFoldingRanges_IncludesRegionsBlocksAndComments()
    {
        const string source = """
            #region Main
            /// First line.
            /// Second line.
            function Run():
                if true then:
                    Print(1);
                endif
            endfunction
            #endregion
            /* a
               b */
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var ranges = RuleScriptLanguageService.GetFoldingRanges(document);

        Assert.Equal(
            [
                new RuleScriptFoldingRange(1, 9, RuleScriptFoldingRangeKind.Region),
                new RuleScriptFoldingRange(2, 3, RuleScriptFoldingRangeKind.Comment),
                new RuleScriptFoldingRange(4, 8, RuleScriptFoldingRangeKind.Block),
                new RuleScriptFoldingRange(5, 7, RuleScriptFoldingRangeKind.Block),
                new RuleScriptFoldingRange(10, 11, RuleScriptFoldingRangeKind.Comment)
            ],
            ranges);
    }

    [Fact]
    public void FindReferences_CoversParallelTaskBodies()
    {
        const string source = """
            parallel:
                task:
                    var item = 1;
                    Print(item);
                endtask
            endparallel
            """;
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var references = RuleScriptLanguageService.FindReferences(document, 3, 13);

        Assert.Equal(2, references.Count);
    }

    [Fact]
    public void TryAnalyzeDocument_WithSourceFile_NavigatesIntoImportsEvenWhenImportIsBroken()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rulescript-lsp-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "eqp");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "lib.rules"), """
                export function Helper():
                    return 1;
                endfunction

                var broken = ;
                """);
            var engine = new RuleScriptEngine { WorkingDirectory = root };
            var document = RuleScriptLanguageService.TryAnalyzeDocument(
                engine,
                "import \"lib\" as lib;\nvar value = lib.Helper();",
                Path.Combine(directory, "main.rules"));

            var definition = RuleScriptLanguageService.GetDefinition(document, 2, 17);

            Assert.NotNull(definition);
            Assert.Equal("lib.Helper", definition.Name);
            Assert.Equal(Path.Combine(directory, "lib.rules"), definition.File);
            Assert.Equal(1, definition.Line);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
