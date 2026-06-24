using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ScopeTests
{
    [Fact]
    public void Function_ReadsGlobalVariable()
    {
        var context = Execute("""
            var base = 10;

            function Add(x):
                return base + x;
            endfunction

            result = Add(5);
            """);

        Assert.Equal(15d, context.Get<double>("result"));
    }

    [Fact]
    public void FunctionAssignment_DoesNotModifyGlobalVariable()
    {
        var context = Execute("""
            var count = 0;

            function Test():
                count = 100;
            endfunction

            Test();
            result = count;
            """);

        Assert.Equal(0d, context.Get<double>("result"));
    }

    [Fact]
    public void Function_CreatesLocalVariable()
    {
        var context = Execute("""
            function Test():
                temp = 123;
                return temp;
            endfunction

            result = Test();
            """);

        Assert.Equal(123d, context.Get<double>("result"));
        Assert.False(context.Contains("temp"));
    }

    [Fact]
    public void FunctionParameterAssignment_StaysLocal()
    {
        var context = Execute("""
            function Add(a, b):
                a = 999;
                return a + b;
            endfunction

            result = Add(1, 2);
            """);

        Assert.Equal(1001d, context.Get<double>("result"));
    }

    [Fact]
    public void NestedFunctionCall_DoesNotLeakLocals()
    {
        var context = Execute("""
            function Inner():
                temp = 10;
                return temp;
            endfunction

            function Outer():
                other = Inner();
                return other + 1;
            endfunction

            result = Outer();
            """);

        Assert.Equal(11d, context.Get<double>("result"));
        Assert.False(context.Contains("temp"));
        Assert.False(context.Contains("other"));
    }

    [Fact]
    public void LocalVariable_ShadowsGlobalVariable()
    {
        var context = Execute("""
            var value = 1;

            function Test():
                var value = 999;
                return value;
            endfunction

            localResult = Test();
            globalResult = value;
            """);

        Assert.Equal(999d, context.Get<double>("localResult"));
        Assert.Equal(1d, context.Get<double>("globalResult"));
    }

    [Fact]
    public void ReturnValue_StillWorks()
    {
        var context = Execute("""
            function Value():
                return "OK";
            endfunction

            result = Value();
            """);

        Assert.Equal("OK", context.Get<string>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }
}
