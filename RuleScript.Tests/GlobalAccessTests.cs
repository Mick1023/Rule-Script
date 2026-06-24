using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class GlobalAccessTests
{
    [Fact]
    public void ReadGlobalVariable()
    {
        var context = Execute("""
            var count = 10;
            result = global.count;
            """);

        Assert.Equal(10d, context.Get<double>("result"));
    }

    [Fact]
    public void WriteGlobalVariable()
    {
        var context = Execute("""
            var count = 0;
            global.count = 100;
            result = count;
            """);

        Assert.Equal(100d, context.Get<double>("result"));
    }

    [Fact]
    public void FunctionModifiesGlobalVariable()
    {
        var context = Execute("""
            var count = 0;

            function Test():
                global.count = 100;
            endfunction

            Test();
            result = count;
            """);

        Assert.Equal(100d, context.Get<double>("result"));
    }

    [Fact]
    public void LocalVariable_DoesNotAffectGlobalAccess()
    {
        var context = Execute("""
            var count = 1;

            function Test():
                var count = 999;
                return global.count;
            endfunction

            result = Test();
            """);

        Assert.Equal(1d, context.Get<double>("result"));
    }

    [Fact]
    public void MissingGlobalVariable_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("result = global.unknown;"));

        Assert.Contains("global", exception.Message);
        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public void NestedFunctionModifiesGlobalVariable()
    {
        var context = Execute("""
            var count = 0;

            function Inner():
                global.count = 42;
            endfunction

            function Outer():
                Inner();
            endfunction

            Outer();
            result = count;
            """);

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public void GlobalReadInsideForeach()
    {
        var context = Execute("""
            var factor = 10;
            var sum = 0;

            foreach item in [1, 2, 3]:
                sum = sum + item + global.factor;
            endforeach

            result = sum;
            """);

        Assert.Equal(36d, context.Get<double>("result"));
    }

    [Fact]
    public void GlobalReadInsideWhile()
    {
        var context = Execute("""
            var limit = 3;
            var count = 0;

            while count < global.limit:
                count = count + 1;
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
