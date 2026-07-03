using RuleScript.Core;
using RuleScript.Core.Formatting;

namespace RuleScript.Tests;

public sealed class Version160ApiTests
{
    [Fact]
    public void RuleScriptFormatter_Format_ExposesFormatterCore()
    {
        const string source = "if true then:\nresult=1;\nendif";

        var formatted = RuleScriptFormatter.Format(source);

        Assert.Equal("if true then:\n    result = 1;\nendif\n", formatted);
        Assert.Equal(formatted, RuleScriptFormatter.Format(formatted));
    }

    [Fact]
    public void LanguageService_GetRegions_ReturnsNestedFoldingMetadata()
    {
        const string source = """
            #region Outer
            #region Inner
            var value = 1;
            #endregion
            #endregion
            """;

        var regions = RuleScriptLanguageService.GetRegions(source);

        Assert.Collection(
            regions,
            outer => Assert.Equal(("Outer", 1, 5), (outer.Name, outer.StartLine, outer.EndLine)),
            inner => Assert.Equal(("Inner", 2, 4), (inner.Name, inner.StartLine, inner.EndLine)));
    }

    [Fact]
    public void LanguageService_GetFunctionDocumentation_ReturnsAssociatedMetadata()
    {
        const string source = """
            /// Gets a player.
            /// @param id Player ID
            function GetPlayer(id):
                return id;
            endfunction
            """;

        var documentation = RuleScriptLanguageService.GetFunctionDocumentation(source, "GetPlayer");

        Assert.Equal("Gets a player.\n@param id Player ID", documentation);
        Assert.Null(RuleScriptLanguageService.GetFunctionDocumentation(source, "Missing"));
    }
}
