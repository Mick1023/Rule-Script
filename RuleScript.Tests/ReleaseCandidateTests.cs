using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ReleaseCandidateTests
{
    [Fact]
    public void GenericEnd_ClosesAllBlockTypes()
    {
        var context = new RuleScriptEngine().Execute("""
            function Sum(values):
                var total = 0;

                foreach item in values:
                    while item > 0:
                        if item == 2 then:
                            total = total + 2;
                        else:
                            total = total + 1;
                        end

                        item = item - 1;
                    end
                end

                return total;
            end

            result = Sum([2, 1]);
            """);

        Assert.Equal(4d, context.Get<double>("result"));
    }

    [Fact]
    public void AlarmProject_FullWorkflow_Works()
    {
        using var project = new RuleScriptProject();
        project.Write("alarm.rules", """
            export function IsAlarm(distance):
                return distance > 500;
            endfunction
            """);
        project.Write("main.rules", """
            import "alarm.rules";

            var payload = JsonParse("{ \"distance\": 519 }");

            if IsAlarm(payload.distance) then:
                global.result = "NG";
            else:
                global.result = "OK";
            endif
            """);

        var context = new RuntimeContext();
        context.Set("result", "");
        new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"), context);

        Assert.Equal("NG", context.Get<string>("result"));
    }

    [Fact]
    public void SensorProject_ImportAliasJsonForeachAndStandardLibrary_Work()
    {
        using var project = new RuleScriptProject();
        project.Write("sensor.rules", """
            export function Payload():
                return JsonParse("{ \"samples\": [100, 200, 300], \"unit\": \"mm\" }");
            endfunction
            """);
        project.Write("main.rules", """
            import "sensor.rules" as sensor;

            var payload = sensor.Payload();
            var total = 0;

            foreach sample in payload.samples:
                total = total + sample;
            endforeach

            result = Join(" ", ["total", ToString(total), payload.unit]);
            """);

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal("total 600 mm", context.Get<string>("result"));
    }

    [Fact]
    public void WarehouseProject_ExecuteFileAndUserFunctions_Work()
    {
        using var project = new RuleScriptProject();
        project.Write("rules.rules", """
            export function NormalizeSku(value):
                return ToUpper(Trim(value));
            endfunction

            export function NeedsReorder(quantity):
                return quantity < 10;
            endfunction
            """);
        project.Write("main.rules", """
            import "rules.rules";

            var sku = NormalizeSku("  ab-100  ");
            var quantity = 8;

            if NeedsReorder(quantity) then:
                result = sku + ":REORDER";
            else:
                result = sku + ":OK";
            endif
            """);

        var context = new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules"));

        Assert.Equal("AB-100:REORDER", context.Get<string>("result"));
    }

    [Fact]
    public void HostFunctionUserFunctionWhileAndGlobal_WorkTogether()
    {
        var engine = new RuleScriptEngine();
        engine.RegisterFunction("Read", _ => 3);

        var context = new RuntimeContext();
        context.Set("count", 0);
        engine.Execute("""
            function AddOne(value):
                return value + 1;
            endfunction

            var i = 0;

            while i < Read():
                global.count = AddOne(global.count);
                i = i + 1;
            endwhile

            result = global.count;
            """, context);

        Assert.Equal(3d, context.Get<double>("result"));
    }

    [Fact]
    public void ImportAlias_DoesNotPolluteGlobalFunctionTable()
    {
        using var project = new RuleScriptProject();
        project.Write("module.rules", "function Hidden(): return 1; endfunction");
        project.Write("main.rules", """
            import "module.rules" as module;

            result = Hidden();
            """);

        Assert.Throws<RuntimeException>(() => new RuleScriptEngine().ExecuteFile(project.PathFor("main.rules")));
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
