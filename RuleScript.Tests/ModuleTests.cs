using RuleScript.Core.Diagnostics;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class ModuleTests
{
    [Fact]
    public void GlobalImport_FunctionCanBeCalledDirectly()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", """
            function IsAlarm(value):
                return value > 500;
            endfunction
            """);
        project.Write("main.rules", """
            import "common.rules";

            result = IsAlarm(600);
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.True(context.Get<bool>("result"));
    }

    [Fact]
    public void ImportedFunction_CanCreateGlobalVariableInCallingContext()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", """
            function SetA():
                global.A = 42;
            endfunction
            """);
        project.Write("main.rules", """
            import "common.rules";

            SetA();
            result = global.A;
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal(42d, context.Get<double>("A"));
        Assert.Equal(42d, context.Get<double>("result"));
    }

    [Fact]
    public void AliasImport_FunctionCanBeCalledThroughAlias()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", """
            function GetSensor():
                return "robot";
            endfunction
            """);
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = robot.GetSensor();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("robot", context.Get<string>("result"));
    }

    [Fact]
    public void AliasImport_DoesNotExposeFunctionGlobally()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", """
            function GetSensor():
                return "robot";
            endfunction
            """);
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = GetSensor();
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("GetSensor", exception.Message);
    }

    [Fact]
    public void TwoAliasImports_CanContainSameFunctionName()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", """
            function GetSensor():
                return "robot";
            endfunction
            """);
        project.Write("port.rules", """
            function GetSensor():
                return "port";
            endfunction
            """);
        project.Write("main.rules", """
            import "robot.rules" as robot;
            import "port.rules" as port;

            var a = robot.GetSensor();
            var b = port.GetSensor();
            result = a + "," + b;
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("robot,port", context.Get<string>("result"));
    }

    [Fact]
    public void DuplicateAlias_ThrowsRuntimeException()
    {
        using var project = new RuleScriptProject();
        project.Write("a.rules", "function A(): return 1; endfunction");
        project.Write("b.rules", "function B(): return 2; endfunction");
        project.Write("main.rules", """
            import "a.rules" as robot;
            import "b.rules" as robot;
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("duplicate import alias", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("robot", exception.Message);
    }

    [Fact]
    public void MissingAliasFunction_ThrowsRuntimeException()
    {
        using var project = new RuleScriptProject();
        project.Write("robot.rules", "function GetSensor(): return \"robot\"; endfunction");
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = robot.Missing();
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("robot", exception.Message);
        Assert.Contains("Missing", exception.Message);
    }

    [Fact]
    public void UnknownAlias_ThrowsRuntimeException()
    {
        using var project = new RuleScriptProject();
        project.Write("main.rules", "result = robot.GetSensor();");

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "main.rules"));

        Assert.Contains("robot", exception.Message);
    }

    [Fact]
    public void AliasImport_WithNestedImport_Works()
    {
        using var project = new RuleScriptProject();
        project.Write("sensor.rules", """
            function Read():
                return "nested";
            endfunction
            """);
        project.Write("robot.rules", """
            import "sensor.rules" as sensor;

            function GetSensor():
                return sensor.Read();
            endfunction
            """);
        project.Write("main.rules", """
            import "robot.rules" as robot;

            result = robot.GetSensor();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("nested", context.Get<string>("result"));
    }

    [Fact]
    public void GlobalImport_AndAliasImport_CanCoexist()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", "function Name(): return \"common\"; endfunction");
        project.Write("robot.rules", "function Name(): return \"robot\"; endfunction");
        project.Write("main.rules", """
            import "common.rules";
            import "robot.rules" as robot;

            result = Name() + "," + robot.Name();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("common,robot", context.Get<string>("result"));
    }

    [Fact]
    public void MainFileFunction_CanOverrideGlobalImportedFunction()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", "function Name(): return \"common\"; endfunction");
        project.Write("main.rules", """
            import "common.rules";

            function Name():
                return "main";
            endfunction

            result = Name();
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("main", context.Get<string>("result"));
    }

    [Fact]
    public void AliasImportedFunction_DoesNotOverrideMainOrGlobalFunction()
    {
        using var project = new RuleScriptProject();
        project.Write("common.rules", "function Name(): return \"common\"; endfunction");
        project.Write("robot.rules", "function Name(): return \"robot\"; endfunction");
        project.Write("main.rules", """
            import "common.rules";
            import "robot.rules" as robot;

            var direct = Name();
            var alias = robot.Name();
            result = direct + "," + alias;
            """);

        var context = ExecuteFile(project, "main.rules");

        Assert.Equal("common,robot", context.Get<string>("result"));
    }

    [Fact]
    public void CircularAliasImport_ThrowsRuntimeException()
    {
        using var project = new RuleScriptProject();
        project.Write("a.rules", """
            import "b.rules" as b;

            function A():
                return b.B();
            endfunction
            """);
        project.Write("b.rules", """
            import "a.rules" as a;

            function B():
                return "b";
            endfunction
            """);

        var exception = Assert.Throws<RuntimeException>(() => ExecuteFile(project, "a.rules"));

        Assert.Contains("Circular import", exception.Message);
    }

    private static RuntimeContext ExecuteFile(RuleScriptProject project, string fileName)
    {
        return new RuleScriptEngine().ExecuteFile(project.PathFor(fileName));
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
