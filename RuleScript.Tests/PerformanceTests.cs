using System.Diagnostics;
using System.Text;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class PerformanceTests
{
    private static readonly TimeSpan GenerousLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public void Execute_OneThousandVariables_Completes()
    {
        var script = new StringBuilder();

        for (var i = 0; i < 1000; i++)
        {
            script.AppendLine($"var v{i} = {i};");
        }

        script.AppendLine("result = v999;");

        var context = ExecuteWithinLimit(script.ToString());

        Assert.Equal(999d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_OneThousandFunctionCalls_Completes()
    {
        var script = new StringBuilder("""
            function Inc(value):
                return value + 1;
            endfunction

            var result = 0;
            """);

        for (var i = 0; i < 1000; i++)
        {
            script.AppendLine("result = Inc(result);");
        }

        var context = ExecuteWithinLimit(script.ToString());

        Assert.Equal(1000d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_OneThousandForeachIterations_Completes()
    {
        var values = string.Join(", ", Enumerable.Repeat("1", 1000));
        var script = $"""
            var values = [{values}];
            var result = 0;

            foreach value in values:
                result = result + value;
            endforeach
            """;

        var context = ExecuteWithinLimit(script);

        Assert.Equal(1000d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_OneThousandWhileIterations_Completes()
    {
        var context = ExecuteWithinLimit("""
            var result = 0;

            while result < 1000:
                result = result + 1;
            endwhile
            """);

        Assert.Equal(1000d, context.Get<double>("result"));
    }

    [Fact]
    public void Execute_OneThousandJsonPathReads_Completes()
    {
        var script = new StringBuilder("""
            var obj = JsonParse("{ \"value\": 1 }");
            var result = 0;
            """);

        for (var i = 0; i < 1000; i++)
        {
            script.AppendLine("result = result + JsonGet(obj, \"value\");");
        }

        var context = ExecuteWithinLimit(script.ToString());

        Assert.Equal(1000d, context.Get<double>("result"));
    }

    [Fact]
    public void ExecuteFile_OneHundredImportedFunctions_Completes()
    {
        using var project = new RuleScriptProject();
        var common = new StringBuilder();
        var main = new StringBuilder("import \"common.rules\";\n\nvar result = 0;\n");

        for (var i = 0; i < 100; i++)
        {
            common.AppendLine($"""
                export function F{i}():
                    return {i};
                endfunction

                """);
            main.AppendLine($"result = result + F{i}();");
        }

        project.Write("common.rules", common.ToString());
        project.Write("main.rules", main.ToString());

        var context = ExecuteFileWithinLimit(project.PathFor("main.rules"));

        Assert.Equal(4950d, context.Get<double>("result"));
    }

    private static RuntimeContext ExecuteWithinLimit(string script)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new RuleScriptEngine().Execute(script);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < GenerousLimit, $"Execution took {stopwatch.Elapsed}.");
        return context;
    }

    private static RuntimeContext ExecuteFileWithinLimit(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new RuleScriptEngine().ExecuteFile(path);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < GenerousLimit, $"Execution took {stopwatch.Elapsed}.");
        return context;
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
