using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class SourceLocationMapperTests
{
    [Fact]
    public void CreateRange_MapsSourceSpanToRuntimeSourceRange()
    {
        var range = RuleScriptSourceMapper.CreateRange("main.rules", new SourceSpan(2, 3, 4, 5));

        Assert.Equal(new RuleScriptSourceRange("main.rules", 2, 3, 4, 5), range);
    }

    [Fact]
    public void CreateTokenRange_UsesExplicitEndOrTokenLengthFallback()
    {
        var explicitEnd = RuleScriptSourceMapper.CreateTokenRange(
            "main.rules",
            line: 1,
            column: 2,
            endLine: 3,
            endColumn: 4,
            tokenText: "ignored");
        var fallbackEnd = RuleScriptSourceMapper.CreateTokenRange(
            "main.rules",
            line: 5,
            column: 6,
            tokenText: "name");

        Assert.Equal(new RuleScriptSourceRange("main.rules", 1, 2, 3, 4), explicitEnd);
        Assert.Equal(new RuleScriptSourceRange("main.rules", 5, 6, 5, 10), fallbackEnd);
    }

    [Fact]
    public void FunctionSymbolLocation_UsesSharedRangeMapping()
    {
        var result = new RuleScriptEngine().Analyze(
            """
            function Format(value):
                return value;
            endfunction
            """);

        var function = Assert.Single(result.Functions, symbol => symbol.Name == "Format");

        Assert.Equal(new RuleScriptSourceLocation(null, 1, 10), function.Location);
        Assert.Equal(new RuleScriptSourceRange(null, 1, 1, 3, 12), function.Range);
    }

    [Fact]
    public void Contains_UsesHalfOpenRangeSemantics()
    {
        var span = new SourceSpan(2, 3, 4, 5);

        Assert.True(RuleScriptSourceMapper.Contains(span, 2, 3));
        Assert.True(RuleScriptSourceMapper.Contains(span, 3, 1));
        Assert.False(RuleScriptSourceMapper.Contains(span, 4, 5));
    }

    [Fact]
    public void Contains_SourceRangeUsesSameHalfOpenSemantics()
    {
        var range = new RuleScriptSourceRange("main.rules", 2, 3, 4, 5);

        Assert.True(RuleScriptSourceMapper.Contains(range, 2, 3));
        Assert.True(RuleScriptSourceMapper.Contains(range, 3, 1));
        Assert.False(RuleScriptSourceMapper.Contains(range, 4, 5));
    }
}
