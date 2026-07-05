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

    private static IReadOnlyList<Statement> Parse(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        return parser.Parse();
    }
}
