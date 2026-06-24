using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuntimeIntegrationTests
{
    [Fact]
    public void Execute_Case1_AddsTwoNumbers()
    {
        var context = Execute("""
            var a = 10;
            var b = 20;
            result = a + b;
            """);

        Assert.Equal(30d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_Case2_ConcatenatesGreeting()
    {
        var context = Execute("""
            var name = "Mick";
            result = "Hello " + name;
            """);

        Assert.Equal("Hello Mick", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_Case3_ScorePasses()
    {
        var context = Execute("""
            var score = 80;

            if score >= 60 then:
                result = "PASS";
            else:
                result = "FAIL";
            endif
            """);

        Assert.Equal("PASS", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_Case4_ScoreFails()
    {
        var context = Execute("""
            var score = 50;

            if score >= 60 then:
                result = "PASS";
            else:
                result = "FAIL";
            endif
            """);

        Assert.Equal("FAIL", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_Case5_ToStringReturnsString()
    {
        var context = Execute("result = ToString(123);");

        Assert.Equal("123", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_ReadmeExample_RunsSuccessfully()
    {
        var context = Execute("""
            var a = 1;
            var b = 2;

            if a + b > 0 then:
                result = "OK";
            else:
                result = "NG";
            endif
            """);

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_BuiltinFunctionWorkflow_ReturnsNg()
    {
        var context = Execute("""
            var raw = "  sr,01,519  ";
            var clean = Trim(raw);
            var upper = ToUpper(clean);
            var distanceText = Substring(upper, 6, 3);
            var distance = ParseInt(distanceText);

            if distance > 500 then:
                result = "NG";
            else:
                result = "OK";
            endif
            """);

        Assert.Equal("NG", context.Get<string>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        var engine = new RuleScriptEngine();
        var context = new RuntimeContext();

        engine.Execute(script, context);

        return context;
    }
}
