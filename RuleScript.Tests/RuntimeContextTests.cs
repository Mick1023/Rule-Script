using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuntimeContextTests
{
    [Fact]
    public void SetAndGet_StoresExternalValues()
    {
        var context = new RuntimeContext();

        context.Set("Name", "Mick");
        context.Set("Age", 30);

        Assert.Equal("Mick", context.Get<string>("Name"));
        Assert.Equal(30, context.Get<int>("Age"));
    }

    [Fact]
    public void Get_WhenVariableDoesNotExist_ThrowsRuntimeException()
    {
        var context = new RuntimeContext();

        Assert.Throws<RuntimeException>(() => context.Get("missing"));
    }
}
