using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class RuleScriptEngineTests
{
    [Fact]
    public void Execute_WithEmptyScript_DoesNotModifyContext()
    {
        var engine = new RuleScriptEngine();
        var context = new RuntimeContext();
        context.Set("Name", "Mick");

        engine.Execute(string.Empty, context);

        Assert.Equal("Mick", context.Get<string>("Name"));
    }
}
