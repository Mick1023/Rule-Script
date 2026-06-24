namespace RuleScript.Core.Parser.Ast;

public sealed record ModuleFunctionCallExpression(
    string ModuleName,
    string FunctionName,
    IReadOnlyList<Expression> Arguments,
    int? Line = null,
    int? Column = null) : Expression;
