namespace RuleScript.Core.Parser.Ast;

public sealed record ConstStatement(
    string Name,
    Expression Initializer,
    int? Line = null,
    int? Column = null) : Statement
{
    public bool IsExported { get; init; }
}
