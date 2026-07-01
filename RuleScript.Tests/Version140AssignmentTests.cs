using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;
using System.Collections.ObjectModel;

namespace RuleScript.Tests;

public sealed class Version140AssignmentTests
{
    [Fact]
    public void Parser_CreatesExpressionTargetAssignments()
    {
        var statements = Parse("""
            obj.name = "New";
            items[0] = value;
            items[0].name = "A";
            """);

        var property = Assert.IsType<TargetAssignmentStatement>(statements[0]);
        var index = Assert.IsType<TargetAssignmentStatement>(statements[1]);
        var nested = Assert.IsType<TargetAssignmentStatement>(statements[2]);

        Assert.IsType<MemberAccessExpression>(property.Target);
        Assert.IsType<IndexExpression>(index.Target);
        Assert.IsType<IndexExpression>(Assert.IsType<MemberAccessExpression>(nested.Target).Target);
    }

    [Fact]
    public void Parser_SimpleVariableAssignmentKeepsExistingAst()
    {
        var statement = Assert.Single(Parse("value = 42;"));

        Assert.IsType<AssignmentStatement>(statement);
    }

    [Fact]
    public void Analyze_AcceptsCompatiblePropertyAndIndexAssignments()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = { name: "Rule" };
            obj.name = "New";
            var items = [{ name: "A" }];
            items[0] = { name: "B" };
            items[0].name = "C";
            """);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Analyze_MissingAssignedPropertyReturnsPropertyNotFound()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var obj = { name: "Rule" };
            obj.missing = "New";
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.PropertyNotFound);
        Assert.Equal("missing", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("var obj = { age: 18 }; obj.age = \"old\";", "age")]
    [InlineData("var items = [1]; items[0] = \"one\";", "[")]
    public void Analyze_IncompatibleAssignedValueReturnsInvalidAssignment(string script, string token)
    {
        var result = new RuleScriptEngine().TryAnalyze(script);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.InvalidAssignment);
        Assert.Equal(token, diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyze_StringIndexReturnsIndexTypeError()
    {
        var result = new RuleScriptEngine().TryAnalyze("""
            var items = [1];
            items["first"] = 2;
            """);

        var diagnostic = Assert.Single(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.IndexTypeError);
        Assert.Equal("[", diagnostic.TokenText);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("(1 + 2) = 3;")]
    [InlineData("var value = 1; value[0] = 2;")]
    public void Analyze_NonAssignableTargetReturnsInvalidAssignment(string script)
    {
        var result = new RuleScriptEngine().TryAnalyze(script);

        Assert.Contains(result.Diagnostics, value =>
            value.Code == RuleScriptDiagnosticCodes.InvalidAssignment);
        Assert.False(result.Success);
    }

    [Fact]
    public void Execute_MutatesObjectPropertyAndArrayElement()
    {
        var context = new RuleScriptEngine().Execute("""
            var obj = { name: "Rule" };
            var items = [1, 2, 3];
            obj.name = "New";
            items[0] = 42;
            name = obj.name;
            first = items[0];
            """);

        Assert.Equal("New", context.Get<string>("name"));
        Assert.Equal(42d, context.Get<double>("first"));
    }

    [Fact]
    public void Execute_MutatesNestedArrayObjectProperty()
    {
        var context = new RuleScriptEngine().Execute("""
            var items = [{ name: "A" }, { name: "B" }];
            items[0].name = "Updated";
            result = items[0].name;
            """);

        Assert.Equal("Updated", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_EvaluatesReceiverIndexAndValueOnceInOrder()
    {
        var calls = new List<string>();
        var items = new List<object?>
        {
            new Dictionary<string, object?> { ["name"] = "A" }
        };
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("GetItems", _ =>
        {
            calls.Add("receiver");
            return items;
        });
        engine.RegisterFunction("GetIndex", _ =>
        {
            calls.Add("index");
            return 0;
        });
        engine.RegisterFunction("GetValue", _ =>
        {
            calls.Add("value");
            return "Updated";
        });

        engine.Execute("GetItems()[GetIndex()].name = GetValue();");

        Assert.Equal(["receiver", "index", "value"], calls);
        var item = Assert.IsType<Dictionary<string, object?>>(items[0]);
        Assert.Equal("Updated", item["name"]);
    }

    [Fact]
    public async Task ExecuteAsync_SupportsNestedTargetAssignment()
    {
        var context = await new RuleScriptEngine().ExecuteAsync("""
            var items = [{ name: "A" }];
            items[0].name = "Async";
            result = items[0].name;
            """);

        Assert.Equal("Async", context.Get<string>("result"));
    }

    [Fact]
    public void Execute_CanAssignWritableHostProperty()
    {
        var person = new WritablePerson { Name = "Before" };
        var context = new RuntimeContext();
        context.Set("person", person);

        new RuleScriptEngine().Execute("person.Name = \"After\";", context);

        Assert.Equal("After", person.Name);
    }

    [Fact]
    public void Execute_ReadonlyHostPropertyThrowsRuntimeException()
    {
        var context = new RuntimeContext();
        context.Set("person", new ReadonlyPerson("Before"));

        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("person.Name = \"After\";", context));

        Assert.Contains("readonly", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Name", exception.TokenText);
    }

    [Fact]
    public void Execute_ReadonlyDictionaryThrowsRuntimeException()
    {
        var values = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>
        {
            ["name"] = "Before"
        });
        var context = new RuntimeContext();
        context.Set("values", values);

        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("values.name = \"After\";", context));

        Assert.Contains("readonly", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Before", values["name"]);
    }

    [Fact]
    public void Execute_InvalidArrayIndexThrowsRuntimeException()
    {
        var exception = Assert.Throws<RuntimeException>(() =>
            new RuleScriptEngine().Execute("var items = [1]; items[1] = 2;"));

        Assert.Contains("outside the array bounds", exception.Message, StringComparison.Ordinal);
        Assert.Equal("[", exception.TokenText);
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        return new Parser(new Lexer(script).Tokenize()).Parse();
    }

    private sealed class WritablePerson
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReadonlyPerson(string name)
    {
        public string Name { get; } = name;
    }
}
