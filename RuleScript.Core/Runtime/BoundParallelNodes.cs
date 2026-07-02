using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed record BoundTaskBlock(TaskBlockSyntax Syntax, RuleScriptTypeInfo ReturnType);

internal sealed record BoundParallelStatement(
    ParallelStatementSyntax Syntax,
    IReadOnlyList<BoundTaskBlock> Tasks);

internal sealed record BoundParallelExpression(
    ParallelExpressionSyntax Syntax,
    IReadOnlyList<BoundTaskBlock> Tasks,
    RuleScriptTypeInfo Type);
