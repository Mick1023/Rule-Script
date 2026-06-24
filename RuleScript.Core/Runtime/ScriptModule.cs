using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed class ScriptModule(string name, IReadOnlyList<Statement> statements)
{
    public string Name { get; } = name;

    public IReadOnlyList<Statement> Statements { get; } = statements;

    public Dictionary<string, UserFunction> Functions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScriptModule> Aliases { get; } = new(StringComparer.Ordinal);
}
