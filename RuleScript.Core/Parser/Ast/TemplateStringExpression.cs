namespace RuleScript.Core.Parser.Ast;

/// <summary>
/// A template string such as <c>`Lot ${id} arrived`</c>. <see cref="Parts"/> holds the literal text
/// around the substitutions and always has one more item than <see cref="Expressions"/>.
/// </summary>
public sealed record TemplateStringExpression(
    IReadOnlyList<string> Parts,
    IReadOnlyList<Expression> Expressions,
    int? Line = null,
    int? Column = null) : Expression;
