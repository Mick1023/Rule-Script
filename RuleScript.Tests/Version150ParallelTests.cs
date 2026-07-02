using System.Diagnostics;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version150ParallelTests
{
    [Fact]
    public void Lexer_RecognizesOnlyParallelKeywords()
    {
        var tokens = new Lexer("parallel task endtask endparallel").Tokenize();
        Assert.Equal(
            [TokenType.Parallel, TokenType.Task, TokenType.EndTask, TokenType.EndParallel, TokenType.EndOfFile],
            tokens.Select(token => token.Type));
    }

    [Theory]
    [InlineData("endtask", "endparallel")]
    [InlineData("end", "end")]
    public void Parser_AcceptsDedicatedAndGenericBlockEnds(string taskEnd, string parallelEnd)
    {
        var statement = Assert.IsType<ParallelStatementSyntax>(Assert.Single(Parse($$"""
            parallel:
                task:
                    value = 1;
                {{taskEnd}}
            {{parallelEnd}}
            """)));

        Assert.Single(statement.Tasks);
        Assert.Single(statement.Tasks[0].Body);
    }

    [Fact]
    public void Parser_RejectsTaskOutsideParallel()
    {
        var exception = Assert.Throws<SyntaxException>(() => Parse("task: value = 1; endtask"));
        Assert.Contains("only allowed", exception.Message);
    }

    [Fact]
    public void Execute_ParallelTasksRunConcurrentlyAndWaitForAll()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Pause", _ => { Thread.Sleep(150); return null; }, threadSafe: true);
        var stopwatch = Stopwatch.StartNew();

        var context = engine.Execute("""
            parallel:
                task:
                    Pause();
                endtask
                task:
                    Pause();
                endtask
            endparallel
            done = true;
            """);

        stopwatch.Stop();
        Assert.True(context.Get<bool>("done"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(280), $"Elapsed: {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ExecuteAsync_ParallelExpressionPreservesDeclarationOrder()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            values = parallel:
                task:
                    return 1;
                endtask
                task:
                    return "two";
                endtask
                task:
                    value = 3;
                endtask
            endparallel;
            """);

        Assert.Equal([1d, "two", null], context.Get<List<object?>>("values"));
    }

    [Fact]
    public void ParallelTaskLocalsAreIsolated()
    {
        var engine = new RuleScriptEngine();
        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("""
            parallel:
                task:
                    localValue = 1;
                endtask
                task:
                    result = localValue;
                endtask
            endparallel
            """));

        Assert.Contains("Parallel task 2 failed", exception.Message);
    }

    [Fact]
    public void Analyze_RejectsHostFunctionNotMarkedThreadSafe()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Unsafe", _ => null);

        var result = engine.TryAnalyze("parallel: task: Unsafe(); endtask endparallel");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe);
    }

    [Fact]
    public void Analyze_RejectsUnsafeHostFunctionReachedThroughUserFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Unsafe", _ => null);

        var result = engine.TryAnalyze("""
            function Wrapper():
                Unsafe();
            endfunction
            parallel:
                task:
                    Wrapper();
                endtask
            endparallel
            """);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            value => value.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe);
        Assert.Equal("Unsafe", diagnostic.TokenText);
    }

    [Fact]
    public void Analyze_RejectsUnsafeHostFunctionReachedThroughMultipleUserFunctions()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Unsafe", _ => null);

        var result = engine.TryAnalyze("""
            function Inner():
                Unsafe();
            endfunction
            function Outer():
                if true then:
                    Inner();
                endif
            endfunction
            parallel:
                task:
                    Outer();
                endtask
            endparallel
            """);

        Assert.Contains(
            result.Diagnostics,
            value => value.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe
                && value.TokenText == "Unsafe");
    }

    [Fact]
    public void Analyze_AllowsThreadSafeHostFunctionReachedThroughUserFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Safe", _ => null, threadSafe: true);

        var result = engine.TryAnalyze("""
            function Wrapper():
                Safe();
            endfunction
            parallel:
                task:
                    Wrapper();
                endtask
            endparallel
            """);

        Assert.DoesNotContain(
            result.Diagnostics,
            value => value.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe);
    }

    [Fact]
    public void Analyze_ParallelReachabilityHandlesUserFunctionCycles()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Unsafe", _ => null);

        var result = engine.TryAnalyze("""
            function First():
                Second();
            endfunction
            function Second():
                First();
                Unsafe();
            endfunction
            parallel:
                task:
                    First();
                endtask
            endparallel
            """);

        Assert.Contains(
            result.Diagnostics,
            value => value.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe
                && value.TokenText == "Unsafe");
    }

    [Fact]
    public void Analyze_AllowsUnsafeHostFunctionWhenUserFunctionIsNotParallelReachable()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Unsafe", _ => null);

        var result = engine.TryAnalyze("""
            function SequentialOnly():
                Unsafe();
            endfunction
            SequentialOnly();
            """);

        Assert.DoesNotContain(
            result.Diagnostics,
            value => value.Code == RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe);
    }

    [Fact]
    public async Task AttributedThreadSafeHostFunctionCanRunInParallel()
    {
        var engine = new RuleScriptEngine();
        Assert.Equal(1, engine.RegisterFunctions(new ParallelHost()));

        var context = await engine.ExecuteAsync("""
            values = parallel:
                task:
                    return LoadValue(1);
                endtask
                task:
                    return LoadValue(2);
                endtask
            endparallel;
            """);

        Assert.Equal([2d, 4d], context.Get<List<object?>>("values"));
    }

    [Fact]
    public async Task Stop_CancelsAllInfiniteParallelTasks()
    {
        var engine = new RuleScriptEngine
        {
            LoopIterationLimitEnabled = false,
            ExecutionTimeoutEnabled = false,
            StatementExecutionLimitEnabled = false
        };
        var execution = engine.ExecuteAsync("""
            parallel:
                task:
                    while true:
                        value = 1;
                    endwhile
                endtask
                task:
                    while true:
                        value = 2;
                    endwhile
                endtask
            endparallel
            """);

        await Task.Delay(100);
        engine.Stop();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    private static IReadOnlyList<Statement> Parse(string source)
    {
        return new Parser(new Lexer(source).Tokenize()).Parse();
    }

    private sealed class ParallelHost
    {
        [RuleScriptFunction(ThreadSafe = true)]
        public double LoadValue(decimal id) => (double)(id * 2);
    }
}
