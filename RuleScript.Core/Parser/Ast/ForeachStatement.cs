namespace RuleScript.Core.Parser.Ast;

public sealed record ForeachStatement(
    string VariableName,
    Expression Iterable,
    IReadOnlyList<Statement> Body,
    int? Line = null,
    int? Column = null) : Statement
{
    /// <summary>
    /// Gets the 1-based line of the iterator variable name. <see cref="Line"/> points at the <c>foreach</c> keyword.
    /// </summary>
    public int? VariableLine { get; init; }

    /// <summary>
    /// Gets the 1-based column of the iterator variable name.
    /// </summary>
    public int? VariableColumn { get; init; }
}
