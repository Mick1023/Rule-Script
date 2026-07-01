using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version140FunctionReturnTypeTests
{
    [Fact]
    public void Analyze_InfersSingleReturnTypeAndCallerType()
    {
        var result = Analyze("""
            function GetAge():
                return 18;
            endfunction
            var age = GetAge();
            """);

        AssertFunctionType(result, "GetAge", RuleScriptValueType.Number);
        AssertVariableType(result, "age", RuleScriptValueType.Number);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_MultipleCompatibleReturnsRemainNonNullableWhenAllPathsReturn()
    {
        var result = Analyze("""
            function Label(enabled: bool):
                if (enabled) then:
                    return "yes";
                else:
                    return "no";
                endif
            endfunction
            """);
        var function = AssertFunctionType(result, "Label", RuleScriptValueType.String);

        Assert.False(function.IsReturnTypeNullable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_MissingReturnPathMakesReturnTypeNullable()
    {
        var result = Analyze("""
            function Find(enabled: bool):
                if (enabled) then:
                    return "value";
                endif
            endfunction
            """);
        var function = AssertFunctionType(result, "Find", RuleScriptValueType.String);

        Assert.True(function.IsReturnTypeNullable);
    }

    [Fact]
    public void Analyze_ExplicitNullAndValueMakesReturnTypeNullable()
    {
        var result = Analyze("""
            function Find(enabled: bool):
                if (enabled) then:
                    return "value";
                else:
                    return null;
                endif
            endfunction
            """);
        var function = AssertFunctionType(result, "Find", RuleScriptValueType.String);

        Assert.True(function.IsReturnTypeNullable);
    }

    [Theory]
    [InlineData("function Nothing(): var value = 1; endfunction")]
    [InlineData("function Nothing(): return; endfunction")]
    public void Analyze_NoValueReturnInfersNull(string script)
    {
        var result = Analyze(script);

        AssertFunctionType(result, "Nothing", RuleScriptValueType.Null);
    }

    [Fact]
    public void Analyze_IncompatibleReturnsProduceGenericTypeDiagnostic()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Invalid(enabled: bool):
                if (enabled) then:
                    return 1;
                else:
                    return "one";
                endif
            endfunction
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.Contains("incompatible value types", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(RuleScriptDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Analyze_FunctionCallsReachFixedPointRegardlessOfDeclarationOrder()
    {
        var result = Analyze("""
            function Outer():
                return Inner();
            endfunction
            function Inner():
                return 42;
            endfunction
            var value = Outer();
            """);

        AssertFunctionType(result, "Outer", RuleScriptValueType.Number);
        AssertFunctionType(result, "Inner", RuleScriptValueType.Number);
        AssertVariableType(result, "value", RuleScriptValueType.Number);
    }

    [Fact]
    public void Analyze_RecursiveFunctionsConvergeFromConcreteBaseReturns()
    {
        var result = Analyze("""
            function IsEven(value: number):
                if (value == 0) then: return true; endif
                return IsOdd(value - 1);
            endfunction
            function IsOdd(value: number):
                if (value == 0) then: return false; endif
                return IsEven(value - 1);
            endfunction
            """);

        AssertFunctionType(result, "IsEven", RuleScriptValueType.Boolean);
        AssertFunctionType(result, "IsOdd", RuleScriptValueType.Boolean);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_NullableFunctionResultParticipatesInNullAccessDiagnostics()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Find(enabled: bool):
                if (enabled) then: return { name: "Rule" }; endif
            endfunction
            var name = Find(true).name;
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RuleScriptDiagnosticCodes.NullAccess);
    }

    [Fact]
    public void Analyze_UserFunctionReturnSupportsExpressionTypeChecking()
    {
        var result = Analyze("""
            function Number(): return 1; endfunction
            var value = Number() + 2;
            """);

        AssertVariableType(result, "value", RuleScriptValueType.Number);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Execute_FunctionRuntimeBehaviorRemainsUnchanged()
    {
        var context = new RuleScriptEngine().Execute("""
            function Value(): return 21; endfunction
            result = Value() * 2;
            """);

        Assert.Equal(42d, context.Get<double>("result"));
    }

    private static RuleScriptAnalysisResult Analyze(string script)
    {
        return new RuleScriptEngine().Analyze(script);
    }

    private static RuleScriptFunctionSymbol AssertFunctionType(
        RuleScriptAnalysisResult result,
        string name,
        RuleScriptValueType type)
    {
        var function = Assert.Single(result.UserFunctions, value => value.Name == name);
        Assert.Equal(type, function.ReturnType);
        return function;
    }

    private static void AssertVariableType(
        RuleScriptAnalysisResult result,
        string name,
        RuleScriptValueType type)
    {
        Assert.Equal(type, Assert.Single(result.Variables, value => value.Name == name).Type);
    }
}
