using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class BuiltinFunctionsTests
{
    [Fact]
    public void ToString_ConvertsNumber()
    {
        var result = Invoke("ToString", 123);

        Assert.Equal("123", result.Value);
    }

    [Fact]
    public void ParseInt_ParsesString()
    {
        var result = Invoke("ParseInt", "123");

        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void ParseInt_InvalidValue_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Invoke("ParseInt", "abc"));
    }

    [Fact]
    public void ParseDecimal_ParsesString()
    {
        var result = Invoke("ParseDecimal", "123.45");

        Assert.Equal(123.45m, result.Value);
    }

    [Fact]
    public void Trim_RemovesWhitespace()
    {
        var result = Invoke("Trim", "  Mick  ");

        Assert.Equal("Mick", result.Value);
    }

    [Fact]
    public void ToUpper_ConvertsString()
    {
        var result = Invoke("ToUpper", "mick");

        Assert.Equal("MICK", result.Value);
    }

    [Fact]
    public void ToLower_ConvertsString()
    {
        var result = Invoke("ToLower", "MICK");

        Assert.Equal("mick", result.Value);
    }

    [Fact]
    public void Replace_ReplacesText()
    {
        var result = Invoke("Replace", "SR,01,519", "SR", "TR");

        Assert.Equal("TR,01,519", result.Value);
    }

    [Fact]
    public void Substring_ExtractsText()
    {
        var result = Invoke("Substring", "SR,01,519", 6, 3);

        Assert.Equal("519", result.Value);
    }

    [Fact]
    public void Substring_OutOfRange_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Invoke("Substring", "abc", 2, 5));
    }

    [Fact]
    public void Length_ReturnsStringLength()
    {
        var result = Invoke("Length", "Mick");

        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void UnknownFunction_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Invoke("Missing"));
    }

    [Fact]
    public void WrongArgumentCount_ThrowsRuntimeException()
    {
        Assert.Throws<RuntimeException>(() => Invoke("ToString", "a", "b"));
    }

    private static RuntimeValue Invoke(string name, params object?[] arguments)
    {
        var functions = new BuiltinFunctions();
        var runtimeArguments = arguments.Select(RuntimeValue.FromObject).ToArray();

        return functions.Invoke(name, runtimeArguments);
    }
}
