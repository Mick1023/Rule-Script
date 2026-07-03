using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160RegionTests
{
    [Fact]
    public void Lexer_RecognizesRegionDirectivesAsTriviaTokens()
    {
        const string source = """
            #region Player
            #endregion
            """;

        var tokens = new Lexer(source).Tokenize();

        Assert.Equal(TokenType.RegionStart, tokens[0].Type);
        Assert.Equal("Player", tokens[0].Literal);
        Assert.Equal(TokenType.RegionEnd, tokens[1].Type);
        Assert.Equal(TokenType.EndOfFile, tokens[2].Type);
    }

    [Fact]
    public void Parser_IgnoresRegionDirectivesAndReturnsMetadata()
    {
        const string source = """
            #region Player
            var value = 1;
            #endregion
            """;

        var parser = new Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        var region = Assert.Single(parser.Regions);

        Assert.Single(statements);
        Assert.Equal("Player", region.Name);
        Assert.Equal(1, region.StartLine);
        Assert.Equal(1, region.StartColumn);
        Assert.Equal(3, region.EndLine);
        Assert.Equal(11, region.EndColumn);
    }

    [Fact]
    public void Parser_ReturnsNestedRegionsWithCorrectRanges()
    {
        const string source = """
            #region Outer
                #region Inner
                var value = 1;
                #endregion
            #endregion
            """;

        var parser = new Parser(new Lexer(source).Tokenize());
        _ = parser.Parse();

        Assert.Collection(
            parser.Regions,
            outer =>
            {
                Assert.Equal("Outer", outer.Name);
                Assert.Equal((1, 1, 5, 11), (outer.StartLine, outer.StartColumn, outer.EndLine, outer.EndColumn));
            },
            inner =>
            {
                Assert.Equal("Inner", inner.Name);
                Assert.Equal((2, 5, 4, 15), (inner.StartLine, inner.StartColumn, inner.EndLine, inner.EndColumn));
            });
    }

    [Fact]
    public void RegionDirectives_DoNotAffectExecution()
    {
        const string source = """
            #region Calculation
            var value = 40;
            result = value + 2;
            #endregion
            """;

        var context = new RuleScriptEngine().Execute(source);

        Assert.Equal(42d, context.Get<double>("result"));
    }
}
