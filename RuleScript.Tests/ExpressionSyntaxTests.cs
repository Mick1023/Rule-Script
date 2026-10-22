using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Formatting;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ExpressionSyntaxTests
{
    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "two")]
    [InlineData(3, "many")]
    public void Conditional_IsRightAssociative(int value, string expected)
    {
        var context = new RuntimeContext();
        context.Set("value", value);

        new RuleScriptEngine().Execute("""var result = value == 1 ? "one" : value == 2 ? "two" : "many";""", context);

        Assert.Equal(expected, context.Get<string>("result"));
    }

    [Fact]
    public void Conditional_EvaluatesOnlySelectedBranch()
    {
        var engine = new RuleScriptEngine();
        var calls = new List<string>();
        engine.RegisterFunction("Track", args =>
        {
            calls.Add((string)args[0]!);
            return args[0];
        });

        var context = engine.Execute("""var result = 1 < 2 ? Track("yes") : Track("no");""");

        Assert.Equal("yes", context.Get<string>("result"));
        Assert.Equal(["yes"], calls);
    }

    [Fact]
    public void Conditional_HasLowerPrecedenceThanNullCoalescing()
    {
        var context = new RuleScriptEngine().Execute("""
            var flag = null;
            var result = flag ?? false ? "set" : "unset";
            """);

        Assert.Equal("unset", context.Get<string>("result"));
    }

    [Fact]
    public void Conditional_WorksInsideObjectLiteralsAndConditions()
    {
        var context = new RuleScriptEngine().Execute("""
            var count = 3;
            var item = {label: count > 1 ? "items" : "item", count: count};
            var plural = false;
            if count > 1 ? true : false then:
                plural = true;
            endif
            """);

        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(context.Get("item"));
        Assert.Equal("items", item["label"]);
        Assert.True(context.Get<bool>("plural"));
    }

    [Fact]
    public void Conditional_NonBoolCondition_ThrowsAtConditionalOperator()
    {
        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("var result = 1 ? 2 : 3;"));

        Assert.Contains("Conditional expression condition must evaluate to a bool value.", exception.Message);
        Assert.Equal(1, exception.Line);
        Assert.Equal(16, exception.Column);
    }

    [Fact]
    public void Conditional_Analysis_ChecksConditionAndMergesBranchTypes()
    {
        var engine = new RuleScriptEngine();

        var mismatch = engine.TryAnalyze("var result = 1 ? 2 : 3;");
        var sameType = engine.TryAnalyze("var flag = true; var result = flag ? 1 : 2;");
        var nullable = engine.TryAnalyze("var flag = true; var result = flag ? \"a\" : null;");

        var diagnostic = Assert.Single(mismatch.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.Contains("Conditional expression condition expects bool", diagnostic.Message);
        Assert.Equal(RuleScriptValueType.Number, Assert.Single(sameType.Symbols.Variables, value => value.Name == "result").Type);
        Assert.Equal(RuleScriptValueType.String, Assert.Single(nullable.Symbols.Variables, value => value.Name == "result").Type);
    }

    [Fact]
    public void Conditional_MissingColon_ReportsSyntaxError()
    {
        var exception = Assert.Throws<SyntaxException>(() =>
            new RuleScriptEngine().Execute("var result = true ? 1;"));

        Assert.Contains("Expected ':' in conditional expression.", exception.Message);
    }

    [Fact]
    public void CompoundAssignment_AppliesEveryOperator()
    {
        var context = new RuleScriptEngine().Execute("""
            var add = 10; add += 5;
            var subtract = 10; subtract -= 4;
            var multiply = 10; multiply *= 3;
            var divide = 10; divide /= 4;
            var remainder = 10; remainder %= 4;
            var text = "Lot"; text += 7;
            """);

        Assert.Equal(15d, context.Get<double>("add"));
        Assert.Equal(6d, context.Get<double>("subtract"));
        Assert.Equal(30d, context.Get<double>("multiply"));
        Assert.Equal(2.5d, context.Get<double>("divide"));
        Assert.Equal(2d, context.Get<double>("remainder"));
        Assert.Equal("Lot7", context.Get<string>("text"));
    }

    [Fact]
    public void CompoundAssignment_UpdatesPropertiesElementsAndGlobals()
    {
        var context = new RuleScriptEngine().Execute("""
            var counter = {hits: 1};
            var totals = [10, 20];
            var total = 0;
            counter.hits += 1;
            totals[1] -= 5;
            function AddToTotal(amount: number) -> void:
                global.total += amount;
                return;
            endfunction
            AddToTotal(7);
            """);

        var counter = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(context.Get("counter"));
        Assert.Equal(2d, Convert.ToDouble(counter["hits"]));
        Assert.Equal(15d, Convert.ToDouble(Assert.IsAssignableFrom<System.Collections.IList>(context.Get("totals"))[1]));
        Assert.Equal(7d, context.Get<double>("total"));
    }

    [Fact]
    public void CompoundAssignment_EvaluatesTargetSubexpressionsOnce()
    {
        var engine = new RuleScriptEngine();
        var calls = 0;
        engine.RegisterFunction("NextIndex", _ =>
        {
            calls++;
            return 0;
        });

        var context = engine.Execute("""
            var items = [1, 2];
            items[NextIndex()] += 10;
            """);

        Assert.Equal(1, calls);
        Assert.Equal(11d, Convert.ToDouble(Assert.IsAssignableFrom<System.Collections.IList>(context.Get("items"))[0]));
    }

    [Fact]
    public async Task NewSyntax_RunsOnAsyncExecutionPath()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunctionAsync("LoadAsync", async (args, cancellationToken) =>
        {
            await Task.Yield();
            return args[0];
        });

        var context = await engine.ExecuteAsync("""
            var items = [1];
            items[0] += LoadAsync(4);
            var label = LoadAsync(true) ? `items: ${items[0]}` : "none";
            """);

        Assert.Equal("items: 5", context.Get<string>("label"));
    }

    [Fact]
    public void CompoundAssignment_UndefinedVariable_Throws()
    {
        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("missing += 1;"));

        Assert.Contains("Undefined variable 'missing'.", exception.Message);
    }

    [Fact]
    public void CompoundAssignment_InvalidTarget_ReportsSyntaxError()
    {
        var exception = Assert.Throws<SyntaxException>(() =>
            new RuleScriptEngine().Execute("Print(1) += 1;"));

        Assert.Contains("Invalid target for '+='", exception.Message);
    }

    [Fact]
    public void CompoundAssignment_Analysis_ReportsReadonlyAndTypeChanges()
    {
        var engine = new RuleScriptEngine();

        var readonlyResult = engine.TryAnalyze("const LIMIT = 1; LIMIT += 1;");
        var typeChange = engine.TryAnalyze("var count = 1; count += \"x\";");
        var valid = engine.TryAnalyze("var count = 1; count += 2; var name = \"a\"; name += 1;");

        Assert.Contains(readonlyResult.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.CannotAssignToReadonly);
        Assert.Contains(typeChange.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.TypeMismatch);
        Assert.DoesNotContain(valid.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
    }

    [Fact]
    public void Parser_CreatesCompoundAssignmentStatement()
    {
        var statement = Assert.IsType<CompoundAssignmentStatement>(Assert.Single(Parse("total -= 2;")));

        Assert.Equal(TokenType.Minus, statement.Operator);
        Assert.Equal("-=", statement.TokenText);
        Assert.IsType<IdentifierExpression>(statement.Target);
    }

    [Fact]
    public void Template_InterpolatesExpressions()
    {
        var context = new RuntimeContext();
        context.Set("lot", new Dictionary<string, object?> { ["id"] = "LOT-1", ["qty"] = 3 });

        new RuleScriptEngine().Execute("""
            var message = `Lot ${lot.id} has ${lot.qty * 2} units${lot.qty > 1 ? "!" : ""}`;
            var nested = `outer ${`inner ${lot.qty}`} end`;
            var braces = `value: ${ {count: 4}.count }`;
            var plain = `no substitutions`;
            var empty = `${null}|${1.5}|${true}`;
            """, context);

        Assert.Equal("Lot LOT-1 has 6 units!", context.Get<string>("message"));
        Assert.Equal("outer inner 3 end", context.Get<string>("nested"));
        Assert.Equal("value: 4", context.Get<string>("braces"));
        Assert.Equal("no substitutions", context.Get<string>("plain"));
        Assert.Equal("|1.5|true", context.Get<string>("empty"));
    }

    [Fact]
    public void Template_SupportsEscapesAndLiteralDollarSigns()
    {
        var context = new RuleScriptEngine().Execute("""
            var text = `cost: $5, \${not}, \`quoted\`, line\nbreak`;
            """);

        Assert.Equal("cost: $5, ${not}, `quoted`, line\nbreak", context.Get<string>("text"));
    }

    [Theory]
    [InlineData("var text = `open;", "Unterminated template string.")]
    [InlineData("var text = `a ${1;", "Unterminated template string substitution.")]
    [InlineData("var text = `a\nb`;", "Unterminated template string.")]
    public void Template_Unterminated_ThrowsSyntaxError(string script, string message)
    {
        var exception = Assert.Throws<SyntaxException>(() => new RuleScriptEngine().Execute(script));

        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void Template_Analysis_IsStringAndChecksSubstitutions()
    {
        var result = new RuleScriptEngine().TryAnalyze("var count = 1; var text = `n=${count} ${Missing()}`;");

        Assert.Equal(RuleScriptValueType.String, Assert.Single(result.Symbols.Variables, value => value.Name == "text").Type);
        Assert.Contains(result.Diagnostics, value => value.Code == RuleScriptDiagnosticCodes.UndefinedFunction);
    }

    [Fact]
    public void Template_TolerantAnalysis_ReportsUnterminatedTemplate()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var count = 1;\nvar text = `n=${count}");

        Assert.False(document.Success);
        Assert.Contains(document.Diagnostics, value => value.Message.Contains("Unterminated template string"));
    }

    [Fact]
    public void Template_NavigationAndRename_SeeSubstitutions()
    {
        const string source = "var count = 1;\nvar text = `n=${count}`;";
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var definition = RuleScriptLanguageService.GetDefinition(document, 2, 17);
        var rename = RuleScriptLanguageService.Rename(document, 1, 5, "total");

        Assert.NotNull(definition);
        Assert.Equal(1, definition.Line);
        Assert.True(rename.Success, rename.ErrorMessage);
        Assert.Contains(rename.Edits, edit => edit.StartLine == 2 && edit.StartColumn == 17);
    }

    [Fact]
    public void Template_Completion_OffersCodeOnlyInsideSubstitutions()
    {
        const string source = "var count = 1;\nvar text = `n=${co} done`;";
        var document = RuleScriptLanguageService.TryAnalyzeDocument(source);

        var inSubstitution = RuleScriptLanguageService.GetCompletions(document, 2, 19);
        var inText = RuleScriptLanguageService.GetCompletions(document, 2, 22);

        Assert.Contains(inSubstitution.Items, item => item.Label == "count");
        Assert.Empty(inText.Items);
    }

    [Fact]
    public void Formatter_SpacesNewOperatorsAndKeepsTemplateText()
    {
        const string source = """
            var a=flag?1:2;
            total+=a;
            var text=`a ${a+1} b ${ {x:1}.x }`;
            var item={label:flag?"y":"n"};
            if flag?true:false then:
            total-=1;
            endif
            """;

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal(NormalizeExpected("""
            var a = flag ? 1 : 2;
            total += a;
            var text = `a ${a + 1} b ${{ x: 1 }.x}`;
            var item = { label: flag ? "y" : "n" };
            if flag ? true : false then:
                total -= 1;
            endif
            """), formatted);
    }

    [Fact]
    public void Formatter_OutputParsesToSameProgram()
    {
        const string source = "var r=x>1?`big ${x}`:x<0?\"neg\":\"small\";r+=`!`;";

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal(2, Parse(formatted).Count);
        Assert.Equal(formatted, RuleScriptFormatterCore.Format(formatted));
    }

    [Theory]
    [InlineData("Range(1, 4)", new[] { 1, 2, 3 })]
    [InlineData("Range(10, -1, -2)", new[] { 10, 8, 6, 4, 2, 0 })]
    [InlineData("Range(0, 10, 4)", new[] { 0, 4, 8 })]
    [InlineData("Range(3, 3)", new int[0])]
    [InlineData("Range(5, 1)", new int[0])]
    public void Range_ReturnsIntegersUpToExclusiveEnd(string call, int[] expected)
    {
        var context = new RuleScriptEngine().Execute($"var values = {call};");

        Assert.Equal(expected.Cast<object?>(), Assert.IsAssignableFrom<System.Collections.IList>(context.Get("values")).Cast<object?>());
    }

    [Fact]
    public void Range_DrivesForeachCountingLoops()
    {
        var context = new RuleScriptEngine().Execute("""
            var total = 0;
            foreach i in Range(1, 11):
                total += i;
            endforeach
            """);

        Assert.Equal(55d, context.Get<double>("total"));
    }

    [Theory]
    [InlineData("Range(0, 5, 0)", "Range argument 'step' cannot be 0.")]
    [InlineData("Range(0.5, 5)", "Range argument 'start' must be an int value.")]
    [InlineData("Range(0, 2000000)", "Range would create 2000000 items, but the maximum is 1000000.")]
    public void Range_InvalidArguments_Throw(string call, string message)
    {
        var exception = Assert.Throws<RuntimeException>(() => new RuleScriptEngine().Execute($"var values = {call};"));

        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void Range_HasTwoBuiltinOverloads()
    {
        var engine = new RuleScriptEngine();

        var analysis = engine.TryAnalyze("var a = Range(0, 3); var b = Range(0, 3, 1); var c = Range(1);");
        var overloads = analysis.Symbols.Functions.Where(function => function.Name == "Range").ToArray();

        Assert.Equal([2, 3], overloads.Select(function => function.Parameters.Count).Order());
        Assert.All(overloads, function => Assert.Equal(RuleScriptFunctionKind.Builtin, function.Kind));
        Assert.Single(analysis.Diagnostics, value => value.Severity == RuleScriptDiagnosticSeverity.Error);
    }

    [Fact]
    public void Range_SignatureHelpSelectsOverloadByArgumentCount()
    {
        var document = RuleScriptLanguageService.TryAnalyzeDocument("var r = Range(0, 10, 2);");

        var help = RuleScriptLanguageService.GetSignatureHelp(document, 1, 22);

        Assert.NotNull(help);
        Assert.Equal(2, help.Signatures.Count);
        Assert.Equal("Range(start: number, end: number, step: number) -> array", help.Signatures[help.ActiveSignature].Label);
        Assert.Equal(2, help.ActiveParameter);
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private static string NormalizeExpected(string value) => value.ReplaceLineEndings("\n") + "\n";
}
