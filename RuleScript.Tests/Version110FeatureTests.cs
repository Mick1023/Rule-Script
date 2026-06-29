using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version110FeatureTests
{
    [Fact]
    public void TryAnalyze_CollectsMultipleParserDiagnosticsAndKeepsSymbols()
    {
        var engine = new RuleScriptEngine();

        var result = engine.TryAnalyze("""
            var first = ;
            var second = ;
            var valid = 3;
            """);

        Assert.False(result.Success);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal(";", diagnostic.TokenText);
            Assert.NotNull(diagnostic.Range);
            Assert.True(diagnostic.Range!.EndColumn > diagnostic.Range.StartColumn);
        });
        Assert.Contains("valid", result.Symbols.VariableNames);
    }

    [Fact]
    public async Task BreakpointPause_ExposesFullStatementSourceRange()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(1);
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("var total = 1 + 2;");
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.NotNull(pause.Range);
        Assert.Equal(1, pause.Range!.StartLine);
        Assert.Equal(1, pause.Range.StartColumn);
        Assert.Equal(1, pause.Range.EndLine);
        Assert.Equal(19, pause.Range.EndColumn);

        session.Continue();
        await runTask.WaitAsync(TestTimeout());
    }

    [Fact]
    public async Task ConditionalBreakpoint_PausesOnlyWhenExpressionIsTrue()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(2, "value == 2");
        engine.AddBreakpoint(3, "value == 2");
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            var value = 1;
            value = 2;
            result = value;
            """);
        var pause = await session.WaitForPauseAsync(TestTimeout());

        Assert.Equal(3, pause.Location.Line);
        Assert.Equal("value == 2", engine.Breakpoints.Single(value => value.Line == 3).Condition);

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());
        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public async Task PauseSnapshot_ExposesFunctionLocalsGlobalsAndCallStack()
    {
        var engine = new RuleScriptEngine();
        engine.AddBreakpoint(3, "local == 42");
        var session = new RuleScriptDebugSession(engine);

        var runTask = session.RunAsync("""
            function Calculate(input):
                var local = input + 1;
                return local;
            endfunction
            result = Calculate(41);
            """);
        await session.WaitForPauseAsync(TestTimeout());
        var snapshot = session.CurrentSnapshot;

        Assert.NotNull(snapshot);
        Assert.Equal(41d, snapshot!.Locals["input"].Value);
        Assert.Equal(42d, snapshot.Locals["local"].Value);
        Assert.Empty(snapshot.Globals);
        Assert.Contains(snapshot.CallStack, frame => frame.EndsWith("::Calculate", StringComparison.Ordinal));

        session.Continue();
        var context = await runTask.WaitAsync(TestTimeout());
        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public void AddBreakpoint_RejectsInvalidCondition()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<SyntaxException>(() => engine.AddBreakpoint(1, "value =="));
    }

    private static CancellationToken TestTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
    }
}
