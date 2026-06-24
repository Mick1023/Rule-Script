using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class CollectionTests
{
    [Fact]
    public void ArrayLiteral_CanBeAssigned()
    {
        var context = Execute("""
            var values = [1, 2, 3];
            result = Length(values);
            """);

        Assert.Equal(3, context.Get<int>("result"));
    }

    [Fact]
    public void ArrayIndexAccess_ReturnsElement()
    {
        var context = Execute("""
            var values = [10, 20, 30];
            result = values[1];
            """);

        Assert.Equal(20d, context.Get<double>("result"));
    }

    [Fact]
    public void ArrayIndexAccess_OutOfRange_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Execute("""
            var values = [10, 20, 30];
            result = values[3];
            """));
    }

    [Fact]
    public void NestedArrayAccess_ReturnsNestedElement()
    {
        var context = Execute("""
            var values = [[1, 2], [3, 4]];
            result = values[1][0];
            """);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void Length_Array_ReturnsCount()
    {
        var context = Execute("""
            var values = [1, 2, 3];
            result = Length(values);
            """);

        Assert.Equal(3, context.Get<int>("result"));
    }

    [Fact]
    public void ObjectPropertyAccess_ReturnsPublicProperty()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetRobot", _ => new Robot("Error", "Too far", new Position(10, 20)));

        var context = engine.Execute("""
            var robot = GetRobot();
            result = robot.Status;
            """);

        Assert.Equal("Error", context.Get<string>("result"));
    }

    [Fact]
    public void NestedPropertyAccess_ReturnsNestedPublicProperty()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetRobot", _ => new Robot("OK", "Ready", new Position(10, 20)));

        var context = engine.Execute("""
            var robot = GetRobot();
            result = robot.Position.X;
            """);

        Assert.Equal(10, context.Get<int>("result"));
    }

    [Fact]
    public void DictionaryPropertyAccess_ReturnsDictionaryValue()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetRobot", _ => new Dictionary<string, object?>
        {
            ["Status"] = "Error",
            ["Message"] = "Too far"
        });

        var context = engine.Execute("""
            var robot = GetRobot();
            result = robot.Status;
            """);

        Assert.Equal("Error", context.Get<string>("result"));
    }

    [Fact]
    public void MissingProperty_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetRobot", _ => new Robot("OK", "Ready", new Position(10, 20)));

        Assert.Throws<RuntimeException>(() => engine.Execute("""
            var robot = GetRobot();
            result = robot.Missing;
            """));
    }

    [Fact]
    public void ArrayWithWhileWorkflow_SumsValues()
    {
        var context = Execute("""
            var values = [1, 2, 3];
            var i = 0;
            var sum = 0;

            while i < Length(values):
                sum = sum + values[i];
                i = i + 1;
            endwhile

            result = sum;
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }

    private sealed record Robot(string Status, string Message, Position Position);

    private sealed record Position(int X, int Y);
}
