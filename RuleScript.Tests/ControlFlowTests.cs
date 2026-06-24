using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ControlFlowTests
{
    [Fact]
    public void While_IncrementsVariable()
    {
        var context = Execute("""
            var i = 0;

            while i < 5:
                i = i + 1;
            endwhile

            result = i;
            """);

        Assert.Equal(5d, context.Get<double>("result"));
    }

    [Fact]
    public void While_FalseSkipsBody()
    {
        var context = Execute("""
            var i = 0;

            while false:
                i = 1;
            endwhile

            result = i;
            """);

        Assert.Equal(0d, context.Get<double>("result"));
    }

    [Fact]
    public void While_ConditionMustBeBool()
    {
        Assert.Throws<RuntimeException>(() => Execute("""
            var i = 0;

            while i:
                i = i + 1;
            endwhile
            """));
    }

    [Fact]
    public void Break_ExitsWhile()
    {
        var context = Execute("""
            var i = 0;

            while true:
                i = i + 1;

                if i == 3 then:
                    break;
                endif
            endwhile

            result = i;
            """);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void Continue_SkipsRestOfLoopBody()
    {
        var context = Execute("""
            var i = 0;
            var sum = 0;

            while i < 5:
                i = i + 1;

                if i == 3 then:
                    continue;
                endif

                sum = sum + i;
            endwhile

            result = sum;
            """);

        Assert.Equal(12d, context.Get<double>("result"));
    }

    [Fact]
    public void NestedWhile_BreakOnlyExitsInnerLoop()
    {
        var context = Execute("""
            var outer = 0;
            var total = 0;

            while outer < 3:
                outer = outer + 1;
                var inner = 0;

                while inner < 5:
                    inner = inner + 1;

                    if inner == 2 then:
                        break;
                    endif
                endwhile

                total = total + inner;
            endwhile

            result = total;
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void NestedWhile_ContinueOnlyAffectsInnerLoop()
    {
        var context = Execute("""
            var outer = 0;
            var total = 0;

            while outer < 2:
                outer = outer + 1;
                var inner = 0;

                while inner < 3:
                    inner = inner + 1;

                    if inner == 2 then:
                        continue;
                    endif

                    total = total + inner;
                endwhile
            endwhile

            result = total;
            """);

        Assert.Equal(8d, context.Get<double>("result"));
    }

    [Fact]
    public void Break_OutsideWhile_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("break;"));
    }

    [Fact]
    public void Continue_OutsideWhile_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("continue;"));
    }

    [Fact]
    public void MaxLoopIterations_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine
        {
            MaxLoopIterations = 3
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("""
            while true:
                Print("tick");
            endwhile
            """));

        Assert.Contains("while", exception.Message);
        Assert.Contains("max loop iterations", exception.Message);
        Assert.Contains("3", exception.Message);
    }

    [Fact]
    public void While_WithHostFunctionConditionWorkflow()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("IsReady", _ => false);

        var context = engine.Execute("""
            var count = 0;

            while IsReady() == false:
                count = count + 1;

                if count >= 3 then:
                    break;
                endif
            endwhile

            result = count;
            """);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }
}
