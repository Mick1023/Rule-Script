using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class StandardLibraryTests
{
    [Fact]
    public void StartsWith_ReturnsTrue()
    {
        var context = Execute("""result = StartsWith("ABC123", "ABC");""");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void EndsWith_ReturnsTrue()
    {
        var context = Execute("""result = EndsWith("ABC123", "123");""");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void Contains_ReturnsTrue()
    {
        var context = Execute("""result = Contains("ABC123", "C1");""");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void Split_ReturnsArray()
    {
        var context = Execute("""
            var values = Split("A,B,C", ",");
            result = values[1];
            """);

        Assert.Equal("B", context.Get<string>("result"));
    }

    [Fact]
    public void Join_ReturnsJoinedString()
    {
        var context = Execute("""result = Join("-", ["A", "B", "C"]);""");

        Assert.Equal("A-B-C", context.Get<string>("result"));
    }

    [Fact]
    public void ArrayAdd_AddsItem()
    {
        var context = Execute("""
            var items = [1, 2];
            ArrayAdd(items, 3);
            result = Length(items);
            """);

        Assert.Equal(3, context.Get<int>("result"));
    }

    [Fact]
    public void ArrayRemove_RemovesItem()
    {
        var context = Execute("""
            var items = [1, 2, 3];
            result = ArrayRemove(items, 2);
            length = Length(items);
            """);

        Assert.True(context.Get<bool>("result"));
        Assert.Equal(2, context.Get<int>("length"));
    }

    [Fact]
    public void ArrayContains_ReturnsTrue()
    {
        var context = Execute("""result = ArrayContains([1, 2, 3], 2);""");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void ArrayClear_RemovesAllItems()
    {
        var context = Execute("""
            var items = [1, 2, 3];
            ArrayClear(items);
            result = Length(items);
            """);

        Assert.Equal(0, context.Get<int>("result"));
    }

    [Fact]
    public void MathFunctions_ReturnExpectedValues()
    {
        var context = Execute("""
            abs = Abs(-5);
            min = Min(10, 3);
            max = Max(10, 3);
            clamp = Clamp(150, 0, 100);
            round = Round(1.6);
            floor = Floor(1.9);
            ceiling = Ceiling(1.1);
            """);

        Assert.Equal(5d, context.Get<double>("abs"));
        Assert.Equal(3d, context.Get<double>("min"));
        Assert.Equal(10d, context.Get<double>("max"));
        Assert.Equal(100d, context.Get<double>("clamp"));
        Assert.Equal(2d, context.Get<double>("round"));
        Assert.Equal(1d, context.Get<double>("floor"));
        Assert.Equal(2d, context.Get<double>("ceiling"));
    }

    [Fact]
    public void ParseBool_ReturnsBool()
    {
        var context = Execute("""result = ParseBool("true");""");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void TypeOf_ReturnsExpectedNames()
    {
        var context = Execute("""
            numberType = TypeOf(1);
            stringType = TypeOf("A");
            boolType = TypeOf(true);
            nullType = TypeOf(JsonParse("null"));
            arrayType = TypeOf([1, 2]);
            objectType = TypeOf(JsonParse("{ \"name\": \"Mick\" }"));
            """);

        Assert.Equal("number", context.Get<string>("numberType"));
        Assert.Equal("string", context.Get<string>("stringType"));
        Assert.Equal("bool", context.Get<string>("boolType"));
        Assert.Equal("null", context.Get<string>("nullType"));
        Assert.Equal("array", context.Get<string>("arrayType"));
        Assert.Equal("object", context.Get<string>("objectType"));
    }

    [Fact]
    public void JsonExists_ReturnsTrueAndFalse()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"robot\": { \"status\": \"OK\" } }");
            exists = JsonExists(obj, "robot.status");
            missing = JsonExists(obj, "robot.unknown");
            """);

        Assert.True(context.Get<bool>("exists"));
        Assert.False(context.Get<bool>("missing"));
    }

    [Fact]
    public void Coalesce_ReturnsFirstNonNullValue()
    {
        var context = Execute("""
            fallback = Coalesce(JsonParse("null"), 123);
            first = Coalesce("A", "B");
            """);

        Assert.Equal(123d, context.Get<double>("fallback"));
        Assert.Equal("A", context.Get<string>("first"));
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }
}
