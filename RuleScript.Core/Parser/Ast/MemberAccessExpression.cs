namespace RuleScript.Core.Parser.Ast;

public sealed record MemberAccessExpression(
    Expression Target,
    string MemberName,
    int? Line = null,
    int? Column = null) : Expression;
