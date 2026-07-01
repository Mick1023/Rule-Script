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
        var globals = knownVariables.ToDictionary(
            value => value.Key,
            value => RuleScriptTypeInfo.From(value.Value),
            StringComparer.Ordinal);
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
            var locals = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);
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

                locals[parameter.Name] = RuleScriptTypeInfo.From(type);
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
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
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

            case DestructuringVarStatement destructuring:
                AnalyzeDestructuringStatement(
                    destructuring,
                    scope,
                    globals,
                    declarations,
                    diagnostics,
                    availableFunctions,
                    userFunctions,
                    hostFunctions);
                break;

            case AssignmentStatement assignment:
                var assignedType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                ReportAssignmentMismatch(assignment.Name, assignedType, scope, assignment.Line, assignment.Column, assignment.SourceSpan, diagnostics);
                scope[assignment.Name] = assignedType;
                break;

            case TargetAssignmentStatement assignment:
                var assignedTargetType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                AnalyzeAssignmentTarget(
                    assignment.Target,
                    assignedTargetType,
                    scope,
                    globals,
                    diagnostics,
                    availableFunctions,
                    userFunctions,
                    hostFunctions,
                    assignment.SourceSpan);
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

            case SwitchStatement switchStatement:
                var switchType = AnalyzeExpression(switchStatement.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                var constants = new Dictionary<SwitchConstant, bool>();
                var analyzedGuards = new HashSet<Expression>(ReferenceEqualityComparer.Instance);

                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var label in switchCase.Labels)
                    {
                        var labelType = AnalyzeExpression(label.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                        if (label.Guard is not null && analyzedGuards.Add(label.Guard))
                        {
                            RequireType(
                                AnalyzeExpression(label.Guard, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions),
                                RuleScriptValueType.Boolean,
                                "Switch case guard",
                                label.Line,
                                label.Column,
                                "when",
                                null,
                                diagnostics);
                        }

                        if (IsKnown(switchType) && IsKnown(labelType) && switchType.Kind != labelType.Kind)
                        {
                            diagnostics.Add(Create(
                                RuleScriptDiagnosticCodes.TypeMismatch,
                                RuleScriptDiagnosticSeverity.Error,
                                $"Switch value has type {RuleScriptTypeFacts.ToDisplayName(switchType.Kind)}, but case label has type {RuleScriptTypeFacts.ToDisplayName(labelType.Kind)}.",
                                label.Line,
                                label.Column,
                                label.TokenText));
                        }

                        if (TryGetSwitchConstant(label.Value, out var constant))
                        {
                            if (constants.TryGetValue(constant, out var hasUnguardedLabel) && hasUnguardedLabel)
                            {
                                diagnostics.Add(Create(
                                    RuleScriptDiagnosticCodes.DuplicateCase,
                                    RuleScriptDiagnosticSeverity.Error,
                                    $"Case label '{label.TokenText}' is unreachable because an earlier unguarded label has the same value.",
                                    label.Line,
                                    label.Column,
                                    label.TokenText));
                            }

                            constants[constant] = hasUnguardedLabel || label.Guard is null;
                        }
                    }

                    AnalyzeChildren(switchCase.Body, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
                }

                if (switchStatement.DefaultBranch is not null)
                {
                    AnalyzeChildren(switchStatement.DefaultBranch, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);
                }
                else
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.MissingDefaultBranch,
                        RuleScriptDiagnosticSeverity.Warning,
                        "Switch statement has no default branch.",
                        switchStatement.Line,
                        switchStatement.Column,
                        "switch",
                        switchStatement.SourceSpan));
                }

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

                if (IsKnown(iterableType) && iterableType.Kind is not RuleScriptValueType.Array and not RuleScriptValueType.String)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.TypeMismatch,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Foreach expects an array or string, but found {RuleScriptTypeFacts.ToDisplayName(iterableType.Kind)}.",
                        loop.Line,
                        loop.Column,
                        "foreach",
                        loop.SourceSpan));
                }

                var hadPrevious = scope.TryGetValue(loop.VariableName, out var previousType);
                scope[loop.VariableName] = iterableType.ElementType ?? RuleScriptTypeInfo.Unknown;
                AnalyzeChildren(loop.Body, scope, globals, declarations, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (hadPrevious)
                {
                    scope[loop.VariableName] = previousType!;
                }
                else
                {
                    scope.Remove(loop.VariableName);
                }

                break;
        }
    }

    private static void AnalyzeDestructuringStatement(
        DestructuringVarStatement statement,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ISet<string> declarations,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        var initializerType = AnalyzeExpression(
            statement.Initializer,
            scope,
            globals,
            diagnostics,
            availableFunctions,
            userFunctions,
            hostFunctions);
        var expectedType = statement.Pattern is ArrayDestructuringPattern
            ? RuleScriptValueType.Array
            : RuleScriptValueType.Object;
        RequireType(
            initializerType,
            expectedType,
            "Destructuring initializer",
            statement.Line,
            statement.Column,
            statement.Pattern is ArrayDestructuringPattern ? "[" : "{",
            statement.SourceSpan,
            diagnostics);

        if (statement.Pattern is ArrayDestructuringPattern
            && statement.Initializer is ArrayExpression array
            && array.Elements.Count < statement.Pattern.Names.Count)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.InvalidAssignment,
                RuleScriptDiagnosticSeverity.Error,
                $"Array destructuring requires {statement.Pattern.Names.Count} value(s), but found {array.Elements.Count}.",
                statement.Line,
                statement.Column,
                "[",
                statement.SourceSpan));
        }

        for (var index = 0; index < statement.Pattern.Names.Count; index++)
        {
            var name = statement.Pattern.Names[index];

            if (!declarations.Add(name))
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.DuplicateDeclaration,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Variable '{name}' is declared more than once in the same scope.",
                    statement.Line,
                    statement.Column,
                    name,
                    statement.SourceSpan));
            }

            var type = statement.Pattern switch
            {
                ArrayDestructuringPattern when initializerType.ElementTypes is not null && index < initializerType.ElementTypes.Count
                    => initializerType.ElementTypes[index],
                ArrayDestructuringPattern => initializerType.ElementType ?? RuleScriptTypeInfo.Unknown,
                ObjectDestructuringPattern when initializerType.TryGetProperty(name, out var propertyType) => propertyType,
                _ => RuleScriptTypeInfo.Unknown
            };

            if (statement.Pattern is ObjectDestructuringPattern
                && initializerType.Properties is not null
                && !initializerType.Properties.ContainsKey(name))
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.PropertyNotFound,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Property '{name}' was not found.",
                    statement.Line,
                    statement.Column,
                    name,
                    statement.SourceSpan));
            }

            scope[name] = type;
        }
    }

    private static void AnalyzeAssignmentTarget(
        Expression target,
        RuleScriptTypeInfo assignedType,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions,
        SourceSpan? span)
    {
        switch (target)
        {
            case IdentifierExpression identifier:
                ReportAssignmentMismatch(identifier.Name, assignedType, scope, identifier.Line, identifier.Column, span, diagnostics);
                scope[identifier.Name] = assignedType;
                return;
            case GlobalIdentifierExpression identifier:
                ReportAssignmentMismatch(identifier.Name, assignedType, globals, identifier.Line, identifier.Column, span, diagnostics);
                globals[identifier.Name] = assignedType;
                return;
            case MemberAccessExpression member:
                var receiverType = AnalyzeExpression(member.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (receiverType.TryGetProperty(member.MemberName, out var propertyType))
                {
                    ReportTargetTypeMismatch(
                        $"Property '{member.MemberName}'",
                        propertyType,
                        assignedType,
                        member.Line,
                        member.Column,
                        member.MemberName,
                        diagnostics);
                    return;
                }

                if (receiverType.Properties is not null)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.PropertyNotFound,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Property '{member.MemberName}' was not found.",
                        member.Line,
                        member.Column,
                        member.MemberName,
                        span));
                    return;
                }

                if (IsKnown(receiverType) && receiverType.Kind != RuleScriptValueType.Object)
                {
                    ReportInvalidAssignment(
                        $"Cannot assign property '{member.MemberName}' on {RuleScriptTypeFacts.ToDisplayName(receiverType.Kind)}.",
                        member.Line,
                        member.Column,
                        member.MemberName,
                        span,
                        diagnostics);
                }

                return;
            case IndexExpression index:
                var receiver = AnalyzeExpression(index.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                var indexType = AnalyzeExpression(index.Index, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (IsKnown(indexType) && indexType.Kind != RuleScriptValueType.Number)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.IndexTypeError,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Array index must be a number, but found {RuleScriptTypeFacts.ToDisplayName(indexType.Kind)}.",
                        index.Line,
                        index.Column,
                        "[",
                        span));
                }

                if (IsKnown(receiver) && receiver.Kind != RuleScriptValueType.Array)
                {
                    ReportInvalidAssignment(
                        $"Index assignment requires an array, but found {RuleScriptTypeFacts.ToDisplayName(receiver.Kind)}.",
                        index.Line,
                        index.Column,
                        "[",
                        span,
                        diagnostics);
                    return;
                }

                if (receiver.ElementType is not null)
                {
                    ReportTargetTypeMismatch(
                        "Array element",
                        receiver.ElementType,
                        assignedType,
                        index.Line,
                        index.Column,
                        "[",
                        diagnostics);
                }

                return;
            default:
                AnalyzeExpression(target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                ReportInvalidAssignment(
                    $"Expression '{target.GetType().Name}' is not assignable.",
                    span?.StartLine,
                    span?.StartColumn,
                    null,
                    span,
                    diagnostics);
                return;
        }
    }

    private static void AnalyzeChildren(
        IEnumerable<Statement> statements,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
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

    private static RuleScriptTypeInfo AnalyzeExpression(
        Expression? expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        switch (expression)
        {
            case null:
                return RuleScriptTypeInfo.From(RuleScriptValueType.Null);
            case LiteralExpression literal:
                return RuleScriptTypeInfo.FromValue(literal.Value);
            case ArrayExpression array:
                return RuleScriptTypeInfo.CreateArray(array.Elements.Select(element =>
                    AnalyzeExpression(element, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions)));
            case ObjectLiteralExpression objectLiteral:
                var properties = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);

                foreach (var property in objectLiteral.Properties)
                {
                    var inferredPropertyType = AnalyzeExpression(property.Value, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                    if (!properties.TryAdd(property.Name, inferredPropertyType))
                    {
                        diagnostics.Add(Create(
                            RuleScriptDiagnosticCodes.DuplicateObjectProperty,
                            RuleScriptDiagnosticSeverity.Error,
                            $"Object property '{property.Name}' is declared more than once.",
                            property.Line,
                            property.Column,
                            property.Name));
                    }
                }

                return RuleScriptTypeInfo.CreateObject(properties);
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
                return RuleScriptTypeInfo.Unknown;
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
                return RuleScriptTypeInfo.Unknown;
            case UnaryExpression unary:
                var operandType = AnalyzeExpression(unary.Operand, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
                var expectedUnaryType = unary.Operator == TokenType.Bang ? RuleScriptValueType.Boolean : RuleScriptValueType.Number;
                RequireType(operandType, expectedUnaryType, $"Operator '{unary.TokenText}'", unary.Line, unary.Column, unary.TokenText, null, diagnostics);
                return RuleScriptTypeInfo.From(expectedUnaryType);
            case BinaryExpression binary:
                return AnalyzeBinary(binary, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case NullCoalescingExpression coalescing:
                return AnalyzeNullCoalescing(coalescing, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case FunctionCallExpression call:
                return AnalyzeFunctionCall(call.Name, call.Arguments, call.Line, call.Column, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case ModuleFunctionCallExpression call:
                return AnalyzeFunctionCall($"{call.ModuleName}.{call.FunctionName}", call.Arguments, call.Line, call.Column, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
            case IndexExpression index:
                var analyzedIndexType = AnalyzeExpression(index.Index, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (IsKnown(analyzedIndexType) && analyzedIndexType.Kind != RuleScriptValueType.Number)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.IndexTypeError,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Array index must be a number, but found {RuleScriptTypeFacts.ToDisplayName(analyzedIndexType.Kind)}.",
                        index.Line,
                        index.Column,
                        "["));
                }

                return AnalyzeExpression(index.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions).ElementType
                    ?? RuleScriptTypeInfo.Unknown;
            case MemberAccessExpression member:
                var targetType = AnalyzeExpression(member.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (targetType.Kind == RuleScriptValueType.Null || targetType.IsNullable)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.NullAccess,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Cannot access property '{member.MemberName}' on {targetType.ToDisplayName()}.",
                        member.Line,
                        member.Column,
                        member.MemberName));
                }

                if (targetType.TryGetProperty(member.MemberName, out var propertyType))
                {
                    return propertyType;
                }

                if (targetType.Properties is not null)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.PropertyNotFound,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Property '{member.MemberName}' was not found.",
                        member.Line,
                        member.Column,
                        member.MemberName));
                }

                return RuleScriptTypeInfo.Unknown;
            case ConditionalMemberAccessExpression member:
                var conditionalTargetType = AnalyzeExpression(member.Target, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

                if (conditionalTargetType.Kind == RuleScriptValueType.Null)
                {
                    return conditionalTargetType;
                }

                if (conditionalTargetType.TryGetProperty(member.MemberName, out var conditionalPropertyType))
                {
                    return conditionalPropertyType.MakeNullable();
                }

                if (conditionalTargetType.Properties is not null
                    || (IsKnown(conditionalTargetType) && conditionalTargetType.Kind != RuleScriptValueType.Object))
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.PropertyNotFound,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Property '{member.MemberName}' was not found.",
                        member.Line,
                        member.Column,
                        member.MemberName));
                }

                return RuleScriptTypeInfo.Unknown.MakeNullable();
            default:
                return RuleScriptTypeInfo.Unknown;
        }
    }

    private static RuleScriptTypeInfo AnalyzeNullCoalescing(
        NullCoalescingExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IReadOnlySet<string> availableFunctions,
        IReadOnlyDictionary<string, RuleScriptFunctionSymbol> userFunctions,
        IReadOnlyDictionary<string, RuleScriptHostFunctionSymbol> hostFunctions)
    {
        var left = AnalyzeExpression(expression.Left, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);
        var right = AnalyzeExpression(expression.Right, scope, globals, diagnostics, availableFunctions, userFunctions, hostFunctions);

        if (IsKnown(left) && !left.CanBeNull)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.InvalidNullCoalescing,
                RuleScriptDiagnosticSeverity.Error,
                $"Left operand of '??' has non-nullable type {left.ToDisplayName()}.",
                expression.Line,
                expression.Column,
                "??"));
            return left;
        }

        if (left.Kind == RuleScriptValueType.Null)
        {
            return right;
        }

        var nonNullLeft = left.WithoutNull();

        if (IsKnown(nonNullLeft)
            && IsKnown(right)
            && right.Kind != RuleScriptValueType.Null
            && nonNullLeft.Kind != right.Kind)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.InvalidNullCoalescing,
                RuleScriptDiagnosticSeverity.Error,
                $"Operator '??' cannot combine {left.ToDisplayName()} and {right.ToDisplayName()}.",
                expression.Line,
                expression.Column,
                "??"));
            return RuleScriptTypeInfo.Unknown;
        }

        if (nonNullLeft.Kind == RuleScriptValueType.Unknown)
        {
            return right;
        }

        if (right.Kind == RuleScriptValueType.Null)
        {
            return nonNullLeft.MakeNullable();
        }

        return nonNullLeft.Kind == right.Kind
            ? right.CanBeNull ? nonNullLeft.MakeNullable() : nonNullLeft
            : RuleScriptTypeInfo.Unknown;
    }

    private static RuleScriptTypeInfo AnalyzeBinary(
        BinaryExpression binary,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
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
            return RuleScriptTypeInfo.From(RuleScriptValueType.Boolean);
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
            return RuleScriptTypeInfo.From(RuleScriptValueType.Boolean);
        }

        if (binary.Operator == TokenType.Plus && (left.Kind == RuleScriptValueType.String || right.Kind == RuleScriptValueType.String))
        {
            return RuleScriptTypeInfo.From(RuleScriptValueType.String);
        }

        if (binary.Operator == TokenType.Plus)
        {
            RequireType(left, RuleScriptValueType.Number, "Operator '+'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
            RequireType(right, RuleScriptValueType.Number, "Operator '+'", binary.Line, binary.Column, binary.TokenText, null, diagnostics);
        }

        return binary.Operator is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            ? RuleScriptTypeInfo.From(RuleScriptValueType.Number)
            : RuleScriptTypeInfo.Unknown;
    }

    private static RuleScriptTypeInfo AnalyzeFunctionCall(
        string name,
        IReadOnlyList<Expression> arguments,
        int? line,
        int? column,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
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
            return RuleScriptTypeInfo.Unknown;
        }

        if (userFunctions.TryGetValue(name, out var userFunction))
        {
            ValidateArguments(name, argumentTypes, userFunction.Parameters, line, column, diagnostics);
        }

        if (hostFunctions.TryGetValue(name, out var hostFunction))
        {
            ValidateArguments(name, argumentTypes, hostFunction.Parameters, line, column, diagnostics);
            return RuleScriptTypeInfo.From(hostFunction.ReturnType);
        }

        return RuleScriptTypeInfo.Unknown;
    }

    private static void ValidateArguments(
        string name,
        IReadOnlyList<RuleScriptTypeInfo> arguments,
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

            if (IsKnown(expected) && IsKnown(actual) && expected != actual.Kind && expected != RuleScriptValueType.Any)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.TypeMismatch,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Function '{name}' argument '{parameters[index].Name}' expects {RuleScriptTypeFacts.ToDisplayName(expected)}, but found {RuleScriptTypeFacts.ToDisplayName(actual.Kind)}.",
                    line,
                    column,
                    name));
            }
        }
    }

    private static void RequireType(
        RuleScriptTypeInfo actual,
        RuleScriptValueType expected,
        string subject,
        int? line,
        int? column,
        string? tokenText,
        SourceSpan? span,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (!IsKnown(actual) || actual.Kind == expected || actual.Kind == RuleScriptValueType.Any)
        {
            return;
        }

        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.TypeMismatch,
            RuleScriptDiagnosticSeverity.Error,
            $"{subject} expects {RuleScriptTypeFacts.ToDisplayName(expected)}, but found {RuleScriptTypeFacts.ToDisplayName(actual.Kind)}.",
            line,
            column,
            tokenText,
            span));
    }

    private static void ReportAssignmentMismatch(
        string name,
        RuleScriptTypeInfo assignedType,
        IDictionary<string, RuleScriptTypeInfo> values,
        int? line,
        int? column,
        SourceSpan? span,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (!values.TryGetValue(name, out var existingType)
            || !IsKnown(existingType)
            || !IsKnown(assignedType)
            || existingType.Kind == assignedType.Kind)
        {
            return;
        }

        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.TypeMismatch,
            RuleScriptDiagnosticSeverity.Error,
            $"Variable '{name}' has type {RuleScriptTypeFacts.ToDisplayName(existingType.Kind)}, but the assigned value has type {RuleScriptTypeFacts.ToDisplayName(assignedType.Kind)}.",
            line,
            column,
            name,
            span));
    }

    private static void ReportTargetTypeMismatch(
        string targetName,
        RuleScriptTypeInfo expectedType,
        RuleScriptTypeInfo assignedType,
        int? line,
        int? column,
        string? tokenText,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (!IsKnown(expectedType)
            || !IsKnown(assignedType)
            || expectedType.Kind == assignedType.Kind)
        {
            return;
        }

        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.InvalidAssignment,
            RuleScriptDiagnosticSeverity.Error,
            $"{targetName} has type {RuleScriptTypeFacts.ToDisplayName(expectedType.Kind)}, but the assigned value has type {RuleScriptTypeFacts.ToDisplayName(assignedType.Kind)}.",
            line,
            column,
            tokenText));
    }

    private static void ReportInvalidAssignment(
        string message,
        int? line,
        int? column,
        string? tokenText,
        SourceSpan? span,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        diagnostics.Add(Create(
            RuleScriptDiagnosticCodes.InvalidAssignment,
            RuleScriptDiagnosticSeverity.Error,
            message,
            line,
            column,
            tokenText,
            span));
    }

    private static bool IsKnown(RuleScriptTypeInfo type) => IsKnown(type.Kind);

    private static bool IsKnown(RuleScriptValueType type) =>
        type is not RuleScriptValueType.Unknown and not RuleScriptValueType.Any;

    private static bool TryGetSwitchConstant(Expression expression, out SwitchConstant constant)
    {
        object? value = expression switch
        {
            LiteralExpression literal => literal.Value,
            UnaryExpression { Operator: TokenType.Minus, Operand: LiteralExpression { Value: double number } } => -number,
            _ => null
        };

        if (expression is not LiteralExpression
            && expression is not UnaryExpression { Operator: TokenType.Minus, Operand: LiteralExpression { Value: double } })
        {
            constant = default;
            return false;
        }

        var type = RuleScriptTypeFacts.FromValue(value);

        if (type is not RuleScriptValueType.Null
            and not RuleScriptValueType.Number
            and not RuleScriptValueType.String
            and not RuleScriptValueType.Boolean)
        {
            constant = default;
            return false;
        }

        constant = new SwitchConstant(type, value);
        return true;
    }

    private readonly record struct SwitchConstant(RuleScriptValueType Type, object? Value);

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
