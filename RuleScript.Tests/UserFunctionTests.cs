using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class UserFunctionTests
{
    [Fact]
    public void Function_ReturnsValue()
    {
        var context = Execute("""
            function Add(a, b):
                return a + b;
            endfunction

            result = Add(10, 20);
            """);

        Assert.Equal(30d, context.Get<double>("result"));
    }

    [Fact]
    public void Function_WithoutReturn_ReturnsNull()
    {
        var context = Execute("""
            function Nothing():
                var value = 123;
            endfunction

            result = Nothing();
            """);

        Assert.Null(context.Get("result"));
    }

    [Fact]
    public void Return_WithoutValue_ReturnsNull()
    {
        var context = Execute("""
            function Nothing():
                return;
            endfunction

            result = Nothing();
            """);

        Assert.Null(context.Get("result"));
    }

    [Fact]
    public void Function_WithNoParameters()
    {
        var context = Execute("""
            function NoArgs():
                return 123;
            endfunction

            result = NoArgs();
            """);

        Assert.Equal(123d, context.Get<double>("result"));
    }

    [Fact]
    public void Function_WithMultipleParameters()
    {
        var context = Execute("""
            function Add(a, b):
                return a + b;
            endfunction

            result = Add(2, 3);
            """);

        Assert.Equal(5d, context.Get<double>("result"));
    }

    [Fact]
    public void UserFunction_CanCallAnotherUserFunction()
    {
        var context = Execute("""
            function Double(value):
                return value * 2;
            endfunction

            function AddDouble(value):
                return Double(value) + 1;
            endfunction

            result = AddDouble(5);
            """);

        Assert.Equal(11d, context.Get<double>("result"));
    }

    [Fact]
    public void UserFunction_CanCallBuiltInFunction()
    {
        var context = Execute("""
            function Clean(value):
                return ToUpper(Trim(value));
            endfunction

            result = Clean("  ok  ");
            """);

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void UserFunction_CanCallHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetValue", _ => 10);

        var context = engine.Execute("""
            function AddHost():
                return GetValue() + 5;
            endfunction

            result = AddHost();
            """);

        Assert.Equal(15d, context.Get<double>("result"));
    }

    [Fact]
    public void UserFunction_OverridesBuiltInFunction()
    {
        var context = Execute("""
            function ToString(value):
                return "custom";
            endfunction

            result = ToString(123);
            """);

        Assert.Equal("custom", context.Get<string>("result"));
    }

    [Fact]
    public void UserFunction_OverridesHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => "host");

        var context = engine.Execute("""
            function Value():
                return "user";
            endfunction

            result = Value();
            """);

        Assert.Equal("user", context.Get<string>("result"));
    }

    [Fact]
    public void LocalVar_DoesNotLeakToGlobalContext()
    {
        var context = Execute("""
            function Test():
                var temp = 123;
                return temp;
            endfunction

            result = Test();
            """);

        Assert.Equal(123d, context.Get<double>("result"));
        Assert.False(context.Contains("temp"));
    }

    [Fact]
    public void Function_CanReadGlobalVariable()
    {
        var context = Execute("""
            var base = 10;

            function AddBase(x):
                return base + x;
            endfunction

            result = AddBase(5);
            """);

        Assert.Equal(15d, context.Get<double>("result"));
    }

    [Fact]
    public void AssignmentInsideFunction_CreatesLocalVariable()
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
    public void AssignmentInsideFunction_UpdatesExistingGlobalVariable()
    {
        var context = Execute("""
            var count = 1;

            function Increment():
                count = count + 1;
            endfunction

            Increment();
            result = count;
            """);

        Assert.Equal(2d, context.Get<double>("result"));
    }

    [Fact]
    public void Recursion_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""
            function Loop():
                return Loop();
            endfunction

            result = Loop();
            """));

        Assert.Contains("Loop", exception.Message);
        Assert.Contains("recursion", exception.Message);
    }

    [Fact]
    public void WrongArgumentCount_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""
            function Add(a, b):
                return a + b;
            endfunction

            result = Add(1);
            """));

        Assert.Contains("Add", exception.Message);
        Assert.Contains("expects 2 argument", exception.Message);
    }

    [Fact]
    public void DuplicateParameterName_ThrowsSyntaxException()
    {
        Assert.Throws<SyntaxException>(() => Parse("""
            function Bad(a, a):
                return a;
            endfunction
            """));
    }

    [Fact]
    public void FunctionDeclarationInsideIf_ThrowsSyntaxException()
    {
        Assert.Throws<SyntaxException>(() => Parse("""
            if true then:
                function Bad():
                    return 1;
                endfunction
            endif
            """));
    }

    [Fact]
    public void MissingEndFunction_ThrowsSyntaxException()
    {
        Assert.Throws<SyntaxException>(() => Parse("""
            function Bad():
                return 1;
            """));
    }

    [Fact]
    public void ReturnOutsideFunction_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("return 1;"));

        Assert.Contains("return", exception.Message);
        Assert.Contains("function", exception.Message);
    }

    [Fact]
    public void BreakInsideFunctionWithoutLocalLoop_ThrowsEvenWhenCalledInsideLoop()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""
            function Stop():
                break;
            endfunction

            while true:
                Stop();
            endwhile
            """));

        Assert.Contains("break", exception.Message);
        Assert.Contains("loop", exception.Message);
    }

    [Fact]
    public void Function_WithForeachAndReturn()
    {
        var context = Execute("""
            function FindFirst(values):
                foreach item in values:
                    if item > 10 then:
                        return item;
                    endif
                endforeach

                return;
            endfunction

            result = FindFirst([1, 20, 30]);
            """);

        Assert.Equal(20d, context.Get<double>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }

    private static void Parse(string script)
    {
        var tokens = new Lexer(script).Tokenize();
        _ = new Parser(tokens).Parse();
    }
}
