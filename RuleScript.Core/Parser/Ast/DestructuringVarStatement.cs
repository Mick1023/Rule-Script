namespace RuleScript.Core.Parser.Ast;

public abstract record DestructuringPattern(IReadOnlyList<string> Names);

public sealed record ArrayDestructuringPattern(IReadOnlyList<string> Elements)
    : DestructuringPattern(Elements);

public sealed record ObjectDestructuringPattern(IReadOnlyList<string> Properties)
    : DestructuringPattern(Properties);

public sealed record DestructuringVarStatement(
    DestructuringPattern Pattern,
    Expression Initializer,
    int? Line = null,
    int? Column = null,
    string? Documentation = null) : Statement;
