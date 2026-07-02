using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ApiStabilizationTests
{
    [Fact]
    public void Execute_NullScript_ThrowsArgumentNullException()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<ArgumentNullException>(() => engine.Execute(null!));
    }

    [Fact]
    public void Execute_EmptyScript_ReturnsEmptyContext()
    {
        var context = new RuleScriptEngine().Execute(string.Empty);

        Assert.Empty(context.Variables);
    }

    [Fact]
    public void Execute_NullContext_ThrowsArgumentNullException()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<ArgumentNullException>(() => engine.Execute("result = 1;", null!));
    }

    [Fact]
    public void ExecuteFile_InvalidPath_ThrowsRuntimeExceptionWithPath()
    {
        var engine = new RuleScriptEngine
        {
            WorkingDirectory = Path.GetTempPath()
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.ExecuteFile("missing-v1.rules"));

        Assert.Contains("ExecuteFile", exception.Message);
        Assert.Contains("missing-v1.rules", exception.Message);
    }

    [Fact]
    public void RegisterFunction_DuplicateHostFunction_Overwrites()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);
        engine.RegisterFunction("Value", _ => 2);

        var context = engine.Execute("result = Value();");

        Assert.Equal(2, context.Get<int>("result"));
    }

    [Fact]
    public void ClearFunctions_RemovesHostFunction()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Value", _ => 1);
        engine.ClearFunctions();

        Assert.Throws<RuntimeException>(() => engine.Execute("result = Value();"));
    }

    [Fact]
    public void ImportResolver_Null_ThrowsArgumentNullException()
    {
        var engine = new RuleScriptEngine();

        Assert.Throws<ArgumentNullException>(() => engine.ImportResolver = null!);
    }

    [Fact]
    public void CustomImportResolver_CanExecuteVirtualFile()
    {
        var engine = new RuleScriptEngine
        {
            ImportResolver = new MemoryImportResolver(new Dictionary<string, string>
            {
                ["virtual:/main.rules"] = "result = 42;"
            })
        };

        var context = engine.ExecuteFile("virtual:/main.rules");

        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RuntimeContext_InvalidName_ThrowsArgumentException(string? name)
    {
        var context = new RuntimeContext();

        Assert.Throws<ArgumentException>(() => context.Set(name!, 1));
        Assert.Throws<ArgumentException>(() => context.Contains(name!));
        Assert.Throws<ArgumentException>(() => context.TryGet(name!, out _));
    }

    [Fact]
    public void RuntimeContext_GetMissing_ThrowsRuntimeException()
    {
        var context = new RuntimeContext();

        Assert.Throws<RuntimeException>(() => context.Get("missing"));
    }

    [Fact]
    public void RuntimeContext_TryGetMissing_ReturnsFalse()
    {
        var context = new RuntimeContext();

        Assert.False(context.TryGet("missing", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void RuntimeContext_VariablesSnapshot_CannotMutateInternalState()
    {
        var context = new RuntimeContext();
        context.Set("value", 1);

        var snapshot = context.Variables;
        context.Set("value", 2);

        Assert.Equal(1, snapshot["value"].Value);
        Assert.Equal(2, context.Get<int>("value"));
    }

    [Fact]
    public void RuntimeContext_Clear_RemovesVariables()
    {
        var context = new RuntimeContext();
        context.Set("value", 1);

        context.Clear();

        Assert.False(context.Contains("value"));
    }

    [Fact]
    public void RuntimeContext_GetOrDefaultTypeMismatch_ReturnsDefault()
    {
        var context = new RuntimeContext();
        context.Set("value", "text");

        Assert.Equal(123, context.GetOrDefault("value", 123));
    }

    [Fact]
    public void SyntaxException_MessageIncludesSyntaxErrorLineColumnAndToken()
    {
        var exception = Assert.Throws<SyntaxException>(() => new RuleScriptEngine().Execute("var = 1;"));

        Assert.Contains("Line", exception.Message);
        Assert.Contains("Column", exception.Message);
        Assert.Contains("Syntax error:", exception.Message);
        Assert.NotNull(exception.TokenText);
    }

    [Fact]
    public void LoopLimitError_IncludesLimitValue()
    {
        var engine = new RuleScriptEngine
        {
            MaxLoopIterations = 3
        };

        var exception = Assert.Throws<RuntimeException>(() => engine.Execute("""
            while true:
            endwhile
            """));

        Assert.Contains("3", exception.Message);
    }

    [Fact]
    public void FullWorkflow_ImportAliasJsonAndForeach()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", """
            export function Payload():
                return JsonParse("{ \"items\": [1, 2, 3] }");
            endfunction
            """);
        project.Write("main.rules", """
            import "robot.rules" as robot;

            var payload = robot.Payload();
            var sum = 0;

            foreach item in payload.items:
                sum = sum + item;
            endforeach

            result = sum;
            """);

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal(6d, context.Get<double>("result"));
    }

    [Fact]
    public void FullWorkflow_HostFunctionUserFunctionAndWhile()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ => 3);

        var context = engine.Execute("""
            function AddOne(value):
                return value + 1;
            endfunction

            var i = 0;
            var result = 0;

            while i < Read():
                result = AddOne(result);
                i = i + 1;
            endwhile
            """);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void ExecuteFile_ProjectExample_Works()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", """
            export function Label(value):
                return "Value:" + ToString(value);
            endfunction
            """);
        project.Write("main.rules", """
            import "common.rules";

            result = Label(5);
            """);

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal("Value:5", context.Get<string>("result"));
    }

    [Fact]
    public void FunctionLocalScope_DoesNotLeak()
    {
        var context = new RuleScriptEngine().Execute("""
            function Test():
                var local = 1;
                return local;
            endfunction

            result = Test();
            """);

        Assert.False(context.Contains("local"));
    }

    [Fact]
    public void GlobalExplicitMutation_Works()
    {
        var context = new RuleScriptEngine().Execute("""
            var count = 0;

            function SetCount():
                global.count = 10;
            endfunction

            SetCount();
            result = count;
            """);

        Assert.Equal(10d, context.Get<double>("result"));
    }

    [Fact]
    public void ImportAlias_DoesNotPolluteGlobalFunctionTable()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", "function Value(): return 1; endfunction");
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = Value();
            """);

        Assert.Throws<RuntimeException>(() => new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules")));
    }

    [Fact]
    public void BuiltInAndStandardLibraryFunctions_StillWork()
    {
        var context = new RuleScriptEngine().Execute("""
            var values = Split("A,B", ",");
            ArrayAdd(values, "C");
            result = Join("-", values);
            starts = StartsWith(result, "A");
            exists = JsonExists(JsonParse("{ \"a\": 1 }"), "a");
            """);

        Assert.Equal("A-B-C", context.Get<string>("result"));
        Assert.True(context.Get<bool>("starts"));
        Assert.True(context.Get<bool>("exists"));
    }

    private sealed class MemoryImportResolver(IReadOnlyDictionary<string, string> files) : IImportResolver
    {
        public string GetFullPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("main.rules", StringComparison.Ordinal)
                ? "virtual:/main.rules"
                : normalized;
        }

        public bool Exists(string path)
        {
            return files.ContainsKey(GetFullPath(path));
        }

        public string ReadAllText(string path)
        {
            return files[GetFullPath(path)];
        }
    }

    private sealed class RuleScriptProject : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rulescript-{Guid.NewGuid():N}");

        public RuleScriptProject()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Write(string fileName, string content)
        {
            File.WriteAllText(PathFor(fileName), content);
        }

        public string PathFor(string fileName)
        {
            return Path.Combine(_directory, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
