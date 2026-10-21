using RuleScript.Core.Lexer;

namespace RuleScript.Core.Parser.Ast;

/// <summary>
/// A compound assignment such as <c>total += amount;</c>. <see cref="Operator"/> is the binary operator
/// applied to the current target value and <see cref="Value"/>, for example <see cref="TokenType.Plus"/> for <c>+=</c>.
/// </summary>
public sealed record CompoundAssignmentStatement(
    Expression Target,
    TokenType Operator,
    Expression Value,
    int? Line = null,
    int? Column = null,
    string? TokenText = null) : Statement;
