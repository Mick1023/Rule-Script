namespace RuleScript.Core.Parser.Ast;

public sealed record ConditionalMemberAccessExpression(
    Expression Target,
    string MemberName,
    int? Line = null,
    int? Column = null) : Expression;
