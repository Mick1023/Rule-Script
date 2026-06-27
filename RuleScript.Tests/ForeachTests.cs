using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ForeachTests
{
    [Fact]
    public void Foreach_SumsArray()
    {
        var context = Execute("""
            var values = [1, 2, 3];
            var sum = 0;

            foreach item in values:
                sum = sum + item;
            endforeach

            result = sum;
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void Foreach_EmptyArraySkipsBody()
    {
        var context = Execute("""
            var values = [];
            var count = 0;

            foreach item in values:
                count = count + 1;
            endforeach

            result = count;
            """);

        Assert.Equal(0d, context.Get<double>("result"));
    }

    [Fact]
    public void Foreach_StringIteratesCharactersAsString()
    {
        var context = Execute("""
            var result = "";

            foreach ch in "ABC":
                result = result + ch + ",";
            endforeach
            """);

        Assert.Equal("A,B,C,", context.Get<string>("result"));
    }

    [Fact]
    public void Foreach_VariableRestoresPreviousValueAfterLoop()
    {
        var context = Execute("""
            var item = "original";
            var values = [1, 2];

            foreach item in values:
                Print(item);
            endforeach

            result = item;
            """);

        Assert.Equal("original", context.Get<string>("result"));
    }

    [Fact]
    public void Foreach_VariableRemovedIfNotExistedBefore()
    {
        var context = Execute("""
            var values = [1, 2];

            foreach item in values:
                Print(item);
            endforeach

            result = "done";
            """);

        Assert.Equal("done", context.Get<string>("result"));
        Assert.False(context.Contains("item"));
    }

    [Fact]
    public void Break_ExitsForeach()
    {
        var context = Execute("""
            var values = [1, 2, 3, 4];
            var sum = 0;

            foreach item in values:
                if item == 3 then:
                    break;
                endif

                sum = sum + item;
            endforeach

            result = sum;
            """);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void Continue_SkipsCurrentForeachIteration()
    {
        var context = Execute("""
            var values = [1, 2, 3, 4];
            var sum = 0;

            foreach item in values:
                if item == 3 then:
                    continue;
                endif

                sum = sum + item;
            endforeach

            result = sum;
            """);

        Assert.Equal(7d, context.Get<double>("result"));
    }

    [Fact]
    public void NestedForeach_BreakExitsInnerLoopOnly()
    {
        var context = Execute("""
            var outerValues = [1, 2];
            var innerValues = [10, 20, 30];
            var sum = 0;

            foreach outer in outerValues:
                foreach inner in innerValues:
                    if inner == 20 then:
                        break;
                    endif

                    sum = sum + inner;
                endforeach
            endforeach

            result = sum;
            """);

        Assert.Equal(20d, context.Get<double>("result"));
    }

    [Fact]
    public void NestedForeach_ContinueAffectsInnerLoopOnly()
    {
        var context = Execute("""
            var outerValues = [1, 2];
            var innerValues = [1, 2, 3];
            var sum = 0;

            foreach outer in outerValues:
                foreach inner in innerValues:
                    if inner == 2 then:
                        continue;
                    endif

                    sum = sum + inner;
                endforeach
            endforeach

            result = sum;
            """);

        Assert.Equal(8d, context.Get<double>("result"));
    }

    [Fact]
    public void Foreach_NonIterableThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("""
            foreach item in 123:
                Print(item);
            endforeach
            """));
    }

    [Fact]
    public void Foreach_MaxLoopIterationsThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine
        {
            MaxLoopIterations = 2
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("""
            var values = [1, 2, 3];

            foreach item in values:
                Print(item);
            endforeach
            """));

        Assert.Contains("foreach", exception.Message);
        Assert.Contains("max loop iterations", exception.Message);
        Assert.Contains("2", exception.Message);
    }

    [Fact]
    public void Foreach_LoopIterationLimitCanBeDisabled()
    {
        var engine = new RuleScriptEngine
        {
            MaxLoopIterations = 2,
            LoopIterationLimitEnabled = false
        };

        var context = engine.Execute("""
            var sum = 0;
            foreach item in [1, 2, 3, 4]:
                sum = sum + item;
            endforeach
            result = sum;
            """);

        Assert.Equal(10d, context.Get<double>("result"));
    }

    [Fact]
    public async Task ForeachAsync_LoopIterationLimitCanBeDisabled()
    {
        var engine = new RuleScriptEngine
        {
            MaxLoopIterations = 2,
            LoopIterationLimitEnabled = false
        };

        var context = await engine.ExecuteAsync("""
            var sum = 0;
            foreach item in [1, 2, 3, 4]:
                sum = sum + item;
            endforeach
            result = sum;
            """);

        Assert.Equal(10d, context.Get<double>("result"));
    }

    [Fact]
    public void Foreach_WithHostReturnedList()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetValues", _ => new List<object?> { 1, 2, 3 });

        var context = engine.Execute("""
            var sum = 0;

            foreach item in GetValues():
                sum = sum + item;
            endforeach

            result = sum;
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void Foreach_WithPropertyAccessItems()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetRobot", _ => new Robot(new List<object?> { 2, 4, 6 }));

        var context = engine.Execute("""
            var robot = GetRobot();
            var sum = 0;

            foreach value in robot.Values:
                sum = sum + value;
            endforeach

            result = sum;
            """);

        Assert.Equal(12d, context.Get<double>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }

    private sealed record Robot(List<object?> Values);
}
