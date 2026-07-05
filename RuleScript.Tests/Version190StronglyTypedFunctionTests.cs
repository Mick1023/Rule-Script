using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version190StronglyTypedFunctionTests
{
    [Fact]
    public void Parse_FunctionDeclarationWithReturnType_SetsReturnTypeName()
    {
        var function = Assert.IsType<FunctionDeclarationStatement>(Assert.Single(Parse("""
            function Add(a: number, b: number) -> number:
                return a + b;
            endfunction
            """)));

        Assert.Equal("Add", function.Name);
        Assert.Equal("number", function.ReturnTypeName);
    }

    [Fact]
    public void Parse_FunctionDeclarationWithoutReturnType_RemainsValid()
    {
        var function = Assert.IsType<FunctionDeclarationStatement>(Assert.Single(Parse("""
            function Test():
            endfunction
            """)));

        Assert.Null(function.ReturnTypeName);
    }

    [Fact]
    public void Analyze_DeclaredReturnTypeUpdatesFunctionSymbol()
    {
        var result = new RuleScriptEngine().Analyze("""
            function Add(a: number, b: number) -> number:
                return a + b;
            endfunction
            """);

        var function = Assert.Single(result.UserFunctions, value => value.Name == "Add");
        Assert.Equal(RuleScriptValueType.Number, function.ReturnType);
        Assert.Equal(RuleScriptValueType.Number, function.DeclaredReturnType);
        Assert.True(function.IsReturnTypeDeclared);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_ReturnTypeMismatchReportsError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Add() -> number:
                return "abc";
            endfunction
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Equal(RuleScriptDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Contains("declares return type number", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_VoidFunctionAllowsEmptyReturn()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Test() -> void:
                return;
            endfunction
            """);

        Assert.DoesNotContain(result.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Equal(RuleScriptValueType.Void, Assert.Single(result.Symbols.UserFunctions).ReturnType);
    }

    [Fact]
    public void Analyze_VoidFunctionRejectsReturnValue()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Test() -> void:
                return 123;
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Error
            && value.Message.Contains("cannot return a value", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DeclaredReturnTypeWarnsWhenNotAllPathsReturn()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Maybe(x: bool) -> number:
                if (x) then:
                    return 1;
                endif
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Warning
            && value.Message == "Not all code paths return a value.");
    }

    [Fact]
    public void Analyze_OldSyntaxWarnsWhenFunctionReturnsValue()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Add(a, b):
                return a + b;
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Warning
            && value.Message.Contains("returns a value but has no declared return type", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_FunctionOverloadsResolveByArgumentType()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Format(value: number) -> string:
                return ToString(value);
            endfunction
            function Format(value: string) -> string:
                return value;
            endfunction
            var numberText = Format(1);
            var stringText = Format("ok");
            """);

        Assert.DoesNotContain(result.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Equal(2, result.Symbols.UserFunctions.Count(value => value.Name == "Format"));
        Assert.Equal(RuleScriptValueType.String, Assert.Single(result.Symbols.Variables, value => value.Name == "numberText").Type);
        Assert.Equal(RuleScriptValueType.String, Assert.Single(result.Symbols.Variables, value => value.Name == "stringText").Type);
    }

    [Fact]
    public void Analyze_DuplicateFunctionSignatureReportsError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Add(left: number, right: number) -> number:
                return left + right;
            endfunction
            function Add(a: number, b: number) -> number:
                return a + b;
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Error
            && value.Message == "Duplicate function signature 'Add(number, number)'.");
    }

    [Fact]
    public void Analyze_FunctionOverloadsMustUseSameReturnType()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Convert(value: number) -> string:
                return ToString(value);
            endfunction
            function Convert(value: string) -> number:
                return Length(value);
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Error
            && value.Message == "Function overloads for 'Convert' must use the same return type.");
    }

    [Fact]
    public void Analyze_NoMatchingOverloadReportsError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Format(value: number) -> string:
                return ToString(value);
            endfunction
            function Format(value: string) -> string:
                return value;
            endfunction
            var text = Format(true);
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Error
            && value.Message == "No matching overload for function 'Format'.");
    }

    [Fact]
    public void Analyze_AmbiguousOverloadReportsError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Pick(value: number) -> string:
                return ToString(value);
            endfunction
            function Pick(value: string) -> string:
                return value;
            endfunction
            function Caller(value):
                return Pick(value);
            endfunction
            """);

        Assert.Contains(result.Diagnostics, value =>
            value.Severity == RuleScriptDiagnosticSeverity.Error
            && value.Message == "Ambiguous overload for function 'Pick'.");
    }

    [Fact]
    public void Analyze_ExactOverloadWinsOverAny()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            function Echo(value: any) -> string:
                return ToString(value);
            endfunction
            function Echo(value: number) -> string:
                return ToString(value);
            endfunction
            var text = Echo(1);
            """);

        Assert.DoesNotContain(result.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Equal(RuleScriptValueType.String, Assert.Single(result.Symbols.Variables, value => value.Name == "text").Type);
    }

    [Fact]
    public void Analyze_UserHostBuiltinFunctionsCanShareNameWithDifferentSignatures()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction(
            "Format",
            [new RuleScriptParameterSymbol("value", RuleScriptValueType.String)],
            RuleScriptValueType.String,
            args => args[0],
            threadSafe: true);

        var result = engine.TryAnalyze("""
            function Format(value: number) -> string:
                return ToString(value);
            endfunction
            var numberText = Format(1);
            var stringText = Format("ok");
            var builtinText = ToString(true);
            """);

        Assert.DoesNotContain(result.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
        Assert.Contains(result.Symbols.Functions, value => value.Name == "Format" && value.Kind == RuleScriptFunctionKind.User);
        Assert.Contains(result.Symbols.Functions, value => value.Name == "Format" && value.Kind == RuleScriptFunctionKind.Host);
        Assert.Contains(result.Symbols.Functions, value => value.Name == "ToString" && value.Kind == RuleScriptFunctionKind.Builtin);
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        return parser.Parse();
    }
}
