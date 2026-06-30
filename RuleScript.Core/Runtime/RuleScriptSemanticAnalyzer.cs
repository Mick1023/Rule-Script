using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal static class RuleScriptSemanticAnalyzer
{
    public static IReadOnlyList<RuleScriptDiagnostic> Analyze(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, RuleScriptValueType> knownVariables,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        var diagnostics = new List<RuleScriptDiagnostic>();
        var globals = new Dictionary<string, RuleScriptValueType>(knownVariables, StringComparer.Ordinal);
        var globalDeclarations = new HashSet<string>(StringComparer.Ordinal);
        var functionDeclarations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var function in statements.OfType<FunctionDeclarationStatement>())
        {
            if (!functionDeclarations.Add(function.Name))
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.DuplicateDeclaration,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Function '{function.Name}' is declared more than once.",
                    function.Line,
                    function.Column,
                    function.Name,
                    function.SourceSpan));
            }
        }

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            AnalyzeStatement(
                statement,
                globals,
                globals,
                globalDeclarations,
                diagnostics,
                availableFunctions,
                userFunctions,
                hostFunctions);
        }

        foreach (var function in statements.OfType<FunctionDeclarationStatement>())
        {
            var locals = new Dictionary<string, RuleScriptValueType>(StringComparer.Ordinal);
            var localDeclarations = new HashSet<string>(StringComparer.Ordinal);

            foreach (var parameter in function.ParameterDefinitions)
            {
                var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                    ? parsed
                    : RuleScriptValueType.Unknown;

                if (!localDeclarations.Add(parameter.Name))
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.DuplicateParameter,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Function '{function.Name}' parameter '{parameter.Name}' is declared more than once.",
                        function.Line,
                        function.Column,
                        parameter.Name,
                        function.SourceSpan));
                }

                locals[parameter.Name] = type;
            }

            foreach (var statement in function.Body)
            {
                AnalyzeStatement(
                    statement,
                    locals,
                    globals,
                    localDeclarations,
                    diagnostics,
                    availableFunctions,
                    userFunctions,
                    hostFunctions);
            }
        }

        return diagnostics;
    }

    private static void AnalyzeStatement(
        Statement statement,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        ISet<string> declarations,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        switch (statement)
        {
            case VarStatement variable:
                var variableType = AnalyzeExpression(variable.Initializer, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (!declarations.Add(variable.Name))
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.DuplicateDeclaration,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Variable '{variable.Name}' is declared more than once in the same scope.",
                        variable.Line,
                        variable.Column,
                        variable.Name,
                        variable.SourceSpan));
                }

                scope[variable.Name] = variableType;
                break;

            case AssignmentStatement assignment:
                var assignedType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                ReportAssignmentMismatch(assignment.Name, assignedType, scope, assignment.Line, assignment.Column, assignment.SourceSpan, diagnostics);
                scope[assignment.Name] = assignedType;
                break;

            case GlobalAssignmentStatement assignment:
                var assignedGlobalType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                ReportAssignmentMismatch(assignment.Name, assignedGlobalType, globals, assignment.Line, assignment.Column, assignment.SourceSpan, diagnostics);
                globals[assignment.Name] = assignedGlobalType;
                break;

            case ExpressionStatement expression:
                AnalyzeExpression(expression.Expression, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                break;

            case ReturnStatement returnStatement:
                AnalyzeExpression(returnStatement.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                break;

            case IfStatement conditional:
                RequireType(
                    AnalyzeExpression(conditional.Condition, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions),
                    RuleScriptValueType.Boolean,
                    "If condition",
                    conditional.Line,
                    conditional.Column,
                    "if",
                    conditional.SourceSpan,
                    diagnostics);
                AnalyzeChildren(conditional.ThenBranch, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
                AnalyzeChildren(conditional.ElseBranch, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
                break;

            case WhileStatement loop:
                RequireType(
                    AnalyzeExpression(loop.Condition, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions),
                    RuleScriptValueType.Boolean,
                    "While condition",
                    loop.Line,
                    loop.Column,
                    "while",
                    loop.SourceSpan,
                    diagnostics);
                AnalyzeChildren(loop.Body, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
                break;

            case ForeachStatement loop:
                var iterableType = AnalyzeExpression(loop.Iterable, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (IsKnown(iterableType) && iterableType is not RuleScriptValueType.Array and not RuleScriptValueType.String)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.TypeMismatch,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Foreach expects an array or string, but found {RuleScriptTypeFacts.ToDisplayName(iterableType)}.",
                        loop.Line,
                        loop.Column,
                        "foreach",
                        loop.SourceSpan));
                }

                RuleScriptValueType? previous = scope.TryGetValue(loop.VariableName, out var previousType)
                    ? previousType
                    : null;
                scope[loop.VariableName] = RuleScriptValueType.Unknown;
                AnalyzeChildren(loop.Body, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (previous.HasValue)
                {
                    scope[loop.VariableName] = previous.Value;
                }
                else
                {
                    scope.Remove(loop.VariableName);
                }

                break;
        }
    }

    private static void AnalyzeChildren(
        IEnumerable<Statement> statements,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        ISet<string> declarations,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        foreach (var statement in statements)
        {
            AnalyzeStatement(statement, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
        }
    }

    private static RuleScriptValueType AnalyzeExpression(
        Expression? expression,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        switch (expression)
        {
            case null:
                return RuleScriptValueType.Null;
            case LiteralExpression literal:
                return RuleScriptTypeFacts.FromValue(literal.Value);
            case ArrayExpression array:
                foreach (var element in array.Elements)
                {
                    AnalyzeExpression(element, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                }

                return RuleScriptValueType.Array;
            case IdentifierExpression identifier:
                if (scope.TryGetValue(identifier.Name, out var localType) || globals.TryGetValue(identifier.Name, out localType))
                {
                    return localType;
                }

                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.UndefinedVariable,
                    RuleScriptDiagnosticSeverity.Warning,
                    $"Variable '{identifier.Name}' is not defined in the current analysis context.",
                    identifier.Line,
                    identifier.Column,
                    identifier.Name));
                return RuleScriptValueType.Unknown;
            case GlobalIdentifierExpression identifier:
                if (globals.TryGetValue(identifier.Name, out var globalType))
                {
                    return globalType;
                }

                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.UndefinedVariable,
                    RuleScriptDiagnosticSeverity.Warning,
                    $"Global variable '{identifier.Name}' is not defined in the current analysis context.",
                    identifier.Line,
                    identifier.Column,
                    identifier.Name));
                return RuleScriptValueType.Unknown;
            case UnaryExpression unary:
                var operandType = AnalyzeExpression(unary.Operand, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                var expectedUnaryType = unary.Operator == TokenType.Bang ? RuleScriptValueType.Boolean : RuleScriptValueType.Number;
                RequireType(operandType, expectedUnaryType, $"Operator '{unary.TokenText}'", unary.Line, unary.Column, unary.TokenText, null, diagnostics);
                return expectedUnaryType;
            case BinaryExpression binary:
                return AnalyzeBinary(binary, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case FunctionCallExpression call:
                return AnalyzeFunctionCall(call.Name, call.Arguments, call.Line, call.Column, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case ModuleFunctionCallExpression call:
                return AnalyzeFunctionCall($"{call.ModuleName}.{call.FunctionName}", call.Arguments, call.Line, call.Column, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case IndexExpression index:
                AnalyzeExpression(index.Index, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                AnalyzeExpression(index.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                return RuleScriptValueType.Unknown;
            case MemberAccessExpression member:
                AnalyzeExpression(member.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                return RuleScriptValueType.Unknown;
            default:
                return RuleScriptValueType.Unknown;
        }
    }

    private static RuleScriptValueType AnalyzeBinary(
        BinaryExpression binary,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        var left = AnalyzeExpression(binary.Left, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
        var right = AnalyzeExpression(binary.Right, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

        if (binary.Operator is TokenType.And or TokenType.Or)
        {
            RequireType(left, RuleScriptValueType.Boolean, $"Operator '{binary.TokenText}'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
            RequireType(right, RuleScriptValueType.Boolean, $"Operator '{binary.TokenText}'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
            return RuleScriptValueType.Boolean;
        }

        if (binary.Operator is TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            or TokenType.Greater or TokenType.GreaterOrEqual or TokenType.Less or TokenType.LessOrEqual)
        {
            RequireType(left, RuleScriptValueType.Number, $"Operator '{binary.TokenText}'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
            RequireType(right, RuleScriptValueType.Number, $"Operator '{binary.TokenText}'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
        }

        if (binary.Operator is TokenType.EqualEqual or TokenType.BangEqual
            or TokenType.Greater or TokenType.GreaterOrEqual or TokenType.Less or TokenType.LessOrEqual)
        {
            return RuleScriptValueType.Boolean;
        }

        if (binary.Operator == TokenType.Plus && (left == RuleScriptValueType.String || right == RuleScriptValueType.String))
        {
            return RuleScriptValueType.String;
        }

        if (binary.Operator == TokenType.Plus)
        {
            RequireType(left, RuleScriptValueType.Number, "Operator '+'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
            RequireType(right, RuleScriptValueType.Number, "Operator '+'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
        }

        return binary.Operator is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            ? RuleScriptValueType.Number
            : RuleScriptValueType.Unknown;
    }

    private static RuleScriptValueType AnalyzeFunctionCall(
        string name,
        IReadOnlyList<Expression> arguments,
        int? line,
        int? column,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        var argumentTypes = arguments
            .Select(argument => AnalyzeExpression(argument, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions))
            .ToArray();

        if (!availableFunctions.Contains(name))
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.UndefinedFunction,
                RuleScriptDiagnosticSeverity.Error,
                $"Function '{name}' is not defined.",
                line,
                column,
                name));
            return RuleScriptValueType.Unknown;
        }

        if (userFunctions.TryGetValue(name, out var userFunction))
        {
            ValidateArguments(name, argumentTypes, userFunction.Parameters, line, column, diagnostics);
        }

        if (hostFunctions.TryGetValue(name, out var hostFunction))
        {
            ValidateArguments(name, argumentTypes, hostFunction.Parameters, line, column, diagnostics);
            return hostFunction.ReturnType;
        }

        return RuleScriptValueType.Unknown;
    }

    private static void ValidateArguments(
        string name,
        IReadOnlyList<RuleScriptValueType> arguments,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        int? line,
        int? column,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        var count = Math.Min(arguments.Count, parameters.Count);

        for (var index = 0; index < count; index++)
        {
            var expected = parameters[index].Type;
            var actual = arguments[index];

            if (IsKnown(expected) && IsKnown(actual) && expected != actual && expected != RuleScriptValueType.Any)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.TypeMismatch,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Function '{name}' argument '{parameters[index].Name}' expects {RuleScriptTypeFacts.ToDisplayName(expected)}, but found {RuleScriptTypeFacts.ToDisplayName(actual)}.",
                    line,
                    column,
                    name));
            }
        }
    }

    private static void RequireType(
        RuleScriptValueType actual,
        RuleScriptValueType expected,
        string subject,
        int? line,
        int? column,
        string? tokenText,
        SourceSpan? span,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (!IsKnown(actual) || actual == expected || actual == RuleScriptValueType.Any)
        {
            return;
        }

        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.TypeMismatch,
            RuleScriptDiagnosticSeverity.Error,
            $"{subject} expects {RuleScriptTypeFacts.ToDisplayName(expected)}, but found {RuleScriptTypeFacts.ToDisplayName(actual)}.",
            line,
            column,
            tokenText,
            span));
    }

    private static void ReportAssignmentMismatch(
        string name,
        RuleScriptValueType assignedType,
        IDictionary<string, RuleScriptValueType> values,
        int? line,
        int? column,
        SourceSpan? span,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (!values.TryGetValue(name, out var existingType)
            || !IsKnown(existingType)
            || !IsKnown(assignedType)
            || existingType == assignedType)
        {
            return;
        }

        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.TypeMismatch,
            RuleScriptDiagnosticSeverity.Error,
            $"Variable '{name}' has type {RuleScriptTypeFacts.ToDisplayName(existingType)}, but the assigned value has type {RuleScriptTypeFacts.ToDisplayName(assignedType)}.",
            line,
            column,
            name,
            span));
    }

    private static bool IsKnown(RuleScriptValueType type) =>
        type is not RuleScriptValueType.Unknown and not RuleScriptValueType.Any;

    private static RuleScriptDiagnostic Create(
        string code,
        RuleScriptDiagnosticSeverity severity,
        string message,
        int? line,
        int? column,
        string? tokenText,
        SourceSpan? span = null)
    {
        RuleScriptSourceRange? range = span is not null
            ? new RuleScriptSourceRange(null, span.StartLine, span.StartColumn, span.EndLine, span.EndColumn)
            : line.HasValue && column.HasValue
                ? new RuleScriptSourceRange(
                    null,
                    line.Value,
                    column.Value,
                    line.Value,
                    column.Value + Math.Max(tokenText?.Length ?? 0, 1))
                : null;

        return new RuleScriptDiagnostic(message, line, column, tokenText, null, range)
        {
            Code = code,
            Severity = severity
        };
    }
}
