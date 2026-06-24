using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class JsonTests
{
    [Fact]
    public void JsonParse_Object()
    {
        var context = Execute("""result = JsonGet(JsonParse("{ \"name\": \"Mick\", \"age\": 30 }"), "name");""");

        Assert.Equal("Mick", context.Get<string>("result"));
    }

    [Fact]
    public void JsonParse_Array()
    {
        var context = Execute("""result = Length(JsonParse("[1, 2, 3]"));""");

        Assert.Equal(3, context.Get<int>("result"));
    }

    [Fact]
    public void JsonParse_NestedObject()
    {
        var context = Execute("""result = JsonGet(JsonParse("{ \"robot\": { \"status\": \"OK\" } }"), "robot.status");""");

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void JsonParse_Null()
    {
        var context = Execute("""result = JsonParse("null");""");

        Assert.Null(context.Get("result"));
    }

    [Fact]
    public void JsonParse_InvalidJson_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""result = JsonParse("{ bad json }");"""));

        Assert.Contains("JsonParse", exception.Message);
    }

    [Fact]
    public void JsonStringify_Object()
    {
        var context = Execute("""result = JsonStringify(JsonParse("{ \"name\": \"Mick\" }"));""");

        Assert.Equal("{\"name\":\"Mick\"}", context.Get<string>("result"));
    }

    [Fact]
    public void JsonStringify_Array()
    {
        var context = Execute("""result = JsonStringify(JsonParse("[1, 2, 3]"));""");

        Assert.Equal("[1,2,3]", context.Get<string>("result"));
    }

    [Fact]
    public void JsonGet_SimpleProperty()
    {
        var context = Execute("""result = JsonGet(JsonParse("{ \"name\": \"Mick\" }"), "name");""");

        Assert.Equal("Mick", context.Get<string>("result"));
    }

    [Fact]
    public void JsonGet_NestedProperty()
    {
        var context = Execute("""result = JsonGet(JsonParse("{ \"robot\": { \"status\": \"OK\" } }"), "robot.status");""");

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void JsonGet_ArrayIndex()
    {
        var context = Execute("""result = JsonGet(JsonParse("{ \"items\": [{ \"name\": \"A\" }] }"), "items.0.name");""");

        Assert.Equal("A", context.Get<string>("result"));
    }

    [Fact]
    public void JsonGet_MissingPath_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""result = JsonGet(JsonParse("{ \"name\": \"Mick\" }"), "missing");"""));

        Assert.Contains("JsonGet", exception.Message);
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void JsonSet_SimpleProperty()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"name\": \"Mick\" }");
            obj = JsonSet(obj, "name", "John");
            result = JsonGet(obj, "name");
            """);

        Assert.Equal("John", context.Get<string>("result"));
    }

    [Fact]
    public void JsonSet_NestedProperty()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"robot\": { \"status\": \"NG\" } }");
            obj = JsonSet(obj, "robot.status", "OK");
            result = JsonGet(obj, "robot.status");
            """);

        Assert.Equal("OK", context.Get<string>("result"));
    }

    [Fact]
    public void JsonSet_ArrayItemProperty()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"items\": [{ \"name\": \"A\" }] }");
            obj = JsonSet(obj, "items.0.name", "B");
            result = JsonGet(obj, "items.0.name");
            """);

        Assert.Equal("B", context.Get<string>("result"));
    }

    [Fact]
    public void JsonSet_MissingPath_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""
            var obj = JsonParse("{ \"name\": \"Mick\" }");
            result = JsonSet(obj, "missing", "John");
            """));

        Assert.Contains("JsonSet", exception.Message);
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void JsonParse_IntegratesWithPropertyAccess()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"name\": \"Mick\", \"items\": [1, 2, 3] }");
            result = obj.name;
            """);

        Assert.Equal("Mick", context.Get<string>("result"));
    }

    [Fact]
    public void JsonParse_IntegratesWithIndexAccess()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"name\": \"Mick\", \"items\": [1, 2, 3] }");
            result = obj.items[1];
            """);

        Assert.Equal(2m, context.Get<decimal>("result"));
    }

    [Fact]
    public void JsonParse_IntegratesWithForeach()
    {
        var context = Execute("""
            var obj = JsonParse("{ \"items\": [1, 2, 3] }");
            var sum = 0;

            foreach item in obj.items:
                sum = sum + item;
            endforeach

            result = sum;
            """);

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void JsonStringify_AfterJsonSet()
    {
        var context = Execute("""
            var data = JsonParse("{ \"name\": \"Mick\" }");
            data = JsonSet(data, "name", "John");
            result = JsonStringify(data);
            """);

        Assert.Equal("{\"name\":\"John\"}", context.Get<string>("result"));
    }

    [Fact]
    public void JsonGet_InvalidArrayIndex_ThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() => Execute("""result = JsonGet(JsonParse("[1, 2]"), "5");"""));

        Assert.Contains("JsonGet", exception.Message);
        Assert.Contains("5", exception.Message);
    }

    [Fact]
    public void JsonStringify_UnsupportedValueType_ThrowsRuntimeException()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetObject", _ => new object());

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("result = JsonStringify(GetObject());"));

        Assert.Contains("JsonStringify", exception.Message);
    }

    private static RuntimeContext Execute(string script)
    {
        return new RuleScriptEngine().Execute(script);
    }
}
