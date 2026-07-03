using RuleScript.Core.Formatting;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160FormatterTests
{
    [Fact]
    public void Format_IfElseIfElse_UsesConsistentBlockIndentation()
    {
        const string source = """
            if value == 1 then:
            result="A";
              elseif value==2 then:
             result = "B";
            else:
            result="C";
             endif
            """;

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal("""
            if value == 1 then:
                result = "A";
            elseif value == 2 then:
                result = "B";
            else:
                result = "C";
            endif
            """ + "\n", formatted);
    }

    [Fact]
    public void Format_WhileForeachAndNestedBlocks_UsesFourSpaces()
    {
        const string source = """
            while index < 2:
            foreach item in items:
            total=total+item;
            endforeach
            index=index+1;
            endwhile
            """;

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal("""
            while index < 2:
                foreach item in items:
                    total = total + item;
                endforeach
                index = index + 1;
            endwhile
            """ + "\n", formatted);
    }

    [Fact]
    public void Format_FunctionModuleExportArrayObjectAndAssignments()
    {
        const string source = """
            import "common.rules" as common;
            export function Update(items):
            var player={name:"Mick",scores:[1,2]};
            player.name="New";
            player.scores[0]=3;
            return player;
            endfunction
            """;

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal("""
            import "common.rules" as common;
            export function Update(items):
                var player = { name: "Mick", scores: [1, 2] };
                player.name = "New";
                player.scores[0] = 3;
                return player;
            endfunction
            """ + "\n", formatted);
    }

    [Theory]
    [InlineData("end", "end")]
    [InlineData("endtask", "endparallel")]
    [InlineData("endtask", "end")]
    public void Format_ParallelTask_PreservesBlockEndStyle(string taskEnd, string parallelEnd)
    {
        var source = $"parallel:\ntask:\nPrint(\"A\");\n{taskEnd}\n{parallelEnd}";

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Equal(
            $"parallel:\n    task:\n        Print(\"A\");\n    {taskEnd}\n{parallelEnd}\n",
            formatted);
    }

    [Fact]
    public void Format_PreservesLineBlockDocumentationAndRegionComments()
    {
        const string source = """
            #region Player
            /// Updates player.
            function Update():
            // before update
            /* block
            comment: ; if */
            Print("update");
            endfunction
            #endregion
            """;

        var formatted = RuleScriptFormatterCore.Format(source);

        Assert.Contains("#region Player", formatted);
        Assert.Contains("/// Updates player.", formatted);
        Assert.Contains("    // before update", formatted);
        Assert.Contains("    /* block\n", formatted);
        Assert.Contains("comment: ; if */", formatted);
        Assert.Contains("#endregion", formatted);
    }

    [Fact]
    public void Format_ResultParsesSuccessfully()
    {
        const string source = "if true then:\nresult={items:[1,2,3]};\nendif";

        var formatted = RuleScriptFormatterCore.Format(source);
        var statements = new Parser(new Lexer(formatted).Tokenize()).Parse();

        Assert.Single(statements);
    }

    [Fact]
    public void Format_PreservesExecutionResult()
    {
        const string source = """
            var total=0;
            foreach item in [1,2,3]:
            total=total+item;
            endforeach
            result=total;
            """;

        var engine = new RuleScriptEngine();
        var before = engine.Execute(source).Get<double>("result");
        var after = engine.Execute(RuleScriptFormatterCore.Format(source)).Get<double>("result");

        Assert.Equal(before, after);
    }
}
