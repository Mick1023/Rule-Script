using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal static class RuleScriptSemanticAnalyzer
{
    private static readonly AsyncLocal<bool> AnalyzingParallelTask = new();

    private readonly record struct FunctionReturnContext(
        string FunctionName,
        RuleScriptValueType DeclaredReturnType);

    private readonly record struct FunctionReturnShape(
        bool HasValueReturn,
        bool AlwaysReturns);

    public static IReadOnlyList<RuleScriptDiagnostic> Analyze(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, RuleScriptValueType> knownVariables,
        IRuleScriptFunctionResolver functionResolver,
        IReadOnlyDictionary<string, RuleScriptTypeInfo>? knownTypeInfos = null)
    {
        var diagnostics = new List<RuleScriptDiagnostic>();
        var globals = knownVariables.ToDictionary(
            value => value.Key,
            value => RuleScriptTypeInfo.From(value.Value),
            StringComparer.Ordinal);
        var globalDeclarations = new HashSet<string>(StringComparer.Ordinal);
        if (knownTypeInfos is not null)
        {
            foreach (var knownType in knownTypeInfos)
            {
                globals[knownType.Key] = knownType.Value;
            }
        }

        var functionSignatures = new Dictionary<string, FunctionDeclarationStatement>(StringComparer.Ordinal);
        var functionReturnTypes = new Dictionary<string, RuleScriptValueType>(StringComparer.Ordinal);

        foreach (var function in statements.OfType<FunctionDeclarationStatement>())
        {
            var parameters = CreateParameterSymbols(function);
            var signature = RuleScriptFunctionSymbol.CreateSignature(function.Name, parameters);
            var signatureKey = RuleScriptFunctionSymbol.CreateSignatureKey(function.Name, parameters);

            if (!functionSignatures.TryAdd(signatureKey, function))
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.DuplicateDeclaration,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Duplicate function signature '{signature}'.",
                    function.Line,
                    function.Column,
                    function.Name,
                    function.SourceSpan));
            }

            if (TryGetDeclaredReturnType(function, out var declaredReturnType)
                && functionReturnTypes.TryGetValue(function.Name, out var existingReturnType)
                && existingReturnType != declaredReturnType)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.TypeMismatch,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Function overloads for '{function.Name}' must use the same return type.",
                    function.Line,
                    function.Column,
                    function.Name,
                    function.SourceSpan));
            }
            else if (declaredReturnType != RuleScriptValueType.Unknown)
            {
                functionReturnTypes.TryAdd(function.Name, declaredReturnType);
            }
        }

        var parallelReachableFunctions = FindParallelReachableFunctions(statements);

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            AnalyzeStatement(
                statement,
                globals,
                globals,
                globalDeclarations,
                diagnostics,
                functionResolver);
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

            var returnContext = TryGetDeclaredReturnType(function, out var declaredReturnType)
                ? new FunctionReturnContext(function.Name, declaredReturnType)
                : (FunctionReturnContext?)null;

            var previousParallelState = AnalyzingParallelTask.Value;
            AnalyzingParallelTask.Value = parallelReachableFunctions.Contains(function.Name);
            try
            {
                foreach (var statement in function.Body)
                {
                    AnalyzeStatement(
                        statement,
                        locals,
                        globals,
                        localDeclarations,
                        diagnostics,
                        functionResolver,
                        returnContext);
                }
            }
            finally
            {
                AnalyzingParallelTask.Value = previousParallelState;
            }

            var returnShape = AnalyzeFunctionReturnShape(function.Body);
            if (returnContext is { } declaredContext)
            {
                if (declaredContext.DeclaredReturnType != RuleScriptValueType.Void
                    && !returnShape.AlwaysReturns)
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.TypeMismatch,
                        RuleScriptDiagnosticSeverity.Warning,
                        "Not all code paths return a value.",
                        function.Line,
                        function.Column,
                        function.Name,
                        function.SourceSpan));
                }
            }
            else if (returnShape.HasValueReturn)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.TypeMismatch,
                    RuleScriptDiagnosticSeverity.Warning,
                    $"Function '{function.Name}' returns a value but has no declared return type. Consider adding '-> number'.",
                    function.Line,
                    function.Column,
                    function.Name,
                    function.SourceSpan));
            }
        }

        var readonlyNames = statements.OfType<ConstStatement>()
            .Select(constant => constant.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (knownTypeInfos is not null)
        {
            readonlyNames.UnionWith(knownTypeInfos.Keys);
        }
        ReportReadonlyAssignments(statements, readonlyNames, diagnostics);

        return diagnostics;
    }

    private static IReadOnlySet<string> FindParallelReachableFunctions(IReadOnlyList<Statement> statements)
    {
        var functions = statements
            .OfType<FunctionDeclarationStatement>()
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        VisitStatements(statements, false, (call, inParallelTask) =>
        {
            if (inParallelTask && functions.ContainsKey(call.Name))
            {
                reachable.Add(call.Name);
            }
        });

        var callGraph = functions.ToDictionary(
            function => function.Key,
            function =>
            {
                var callees = new HashSet<string>(StringComparer.Ordinal);
                VisitStatements(function.Value.Body, false, (call, _) =>
                {
                    if (functions.ContainsKey(call.Name))
                    {
                        callees.Add(call.Name);
                    }
                });
                return callees;
            },
            StringComparer.Ordinal);
        var pending = new Queue<string>(reachable);

        while (pending.TryDequeue(out var functionName))
        {
            foreach (var callee in callGraph[functionName])
            {
                if (reachable.Add(callee))
                {
                    pending.Enqueue(callee);
                }
            }
        }

        return reachable;
    }

    private static void VisitStatements(
        IEnumerable<Statement> statements,
        bool inParallelTask,
        Action<FunctionCallExpression, bool> visitCall)
    {
        foreach (var statement in statements)
        {
            VisitStatement(statement, inParallelTask, visitCall);
        }
    }

    private static void VisitStatement(
        Statement statement,
        bool inParallelTask,
        Action<FunctionCallExpression, bool> visitCall)
    {
        switch (statement)
        {
            case VarStatement variable:
                VisitExpression(variable.Initializer, inParallelTask, visitCall);
                break;
            case ConstStatement constant:
                VisitExpression(constant.Initializer, inParallelTask, visitCall);
                break;
            case DestructuringVarStatement destructuring:
                VisitExpression(destructuring.Initializer, inParallelTask, visitCall);
                break;
            case AssignmentStatement assignment:
                VisitExpression(assignment.Value, inParallelTask, visitCall);
                break;
            case TargetAssignmentStatement assignment:
                VisitExpression(assignment.Target, inParallelTask, visitCall);
                VisitExpression(assignment.Value, inParallelTask, visitCall);
                break;
            case GlobalAssignmentStatement assignment:
                VisitExpression(assignment.Value, inParallelTask, visitCall);
                break;
            case ExpressionStatement expression:
                VisitExpression(expression.Expression, inParallelTask, visitCall);
                break;
            case ReturnStatement returned:
                VisitExpression(returned.Value, inParallelTask, visitCall);
                break;
            case IfStatement conditional:
                VisitExpression(conditional.Condition, inParallelTask, visitCall);
                VisitStatements(conditional.ThenBranch, inParallelTask, visitCall);
                VisitStatements(conditional.ElseBranch, inParallelTask, visitCall);
                break;
            case WhileStatement loop:
                VisitExpression(loop.Condition, inParallelTask, visitCall);
                VisitStatements(loop.Body, inParallelTask, visitCall);
                break;
            case ForeachStatement loop:
                VisitExpression(loop.Iterable, inParallelTask, visitCall);
                VisitStatements(loop.Body, inParallelTask, visitCall);
                break;
            case SwitchStatement switchStatement:
                VisitExpression(switchStatement.Value, inParallelTask, visitCall);
                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var label in switchCase.Labels)
                    {
                        VisitExpression(label.Value, inParallelTask, visitCall);
                        VisitExpression(label.Guard, inParallelTask, visitCall);
                    }
                    VisitStatements(switchCase.Body, inParallelTask, visitCall);
                }
                if (switchStatement.DefaultBranch is not null)
                {
                    VisitStatements(switchStatement.DefaultBranch, inParallelTask, visitCall);
                }
                break;
            case ParallelStatementSyntax parallel:
                foreach (var task in parallel.Tasks)
                {
                    VisitStatements(task.Body, true, visitCall);
                }
                break;
            case FunctionDeclarationStatement function:
                VisitStatements(function.Body, false, visitCall);
                break;
        }
    }

    private static void VisitExpression(
        Expression? expression,
        bool inParallelTask,
        Action<FunctionCallExpression, bool> visitCall)
    {
        switch (expression)
        {
            case null:
            case LiteralExpression:
            case IdentifierExpression:
            case GlobalIdentifierExpression:
                return;
            case FunctionCallExpression call:
                visitCall(call, inParallelTask);
                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, inParallelTask, visitCall);
                }
                return;
            case ModuleFunctionCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, inParallelTask, visitCall);
                }
                return;
            case UnaryExpression unary:
                VisitExpression(unary.Operand, inParallelTask, visitCall);
                return;
            case BinaryExpression binary:
                VisitExpression(binary.Left, inParallelTask, visitCall);
                VisitExpression(binary.Right, inParallelTask, visitCall);
                return;
            case NullCoalescingExpression coalescing:
                VisitExpression(coalescing.Left, inParallelTask, visitCall);
                VisitExpression(coalescing.Right, inParallelTask, visitCall);
                return;
            case ArrayExpression array:
                foreach (var element in array.Elements)
                {
                    VisitExpression(element, inParallelTask, visitCall);
                }
                return;
            case ObjectLiteralExpression objectLiteral:
                foreach (var property in objectLiteral.Properties)
                {
                    VisitExpression(property.Value, inParallelTask, visitCall);
                }
                return;
            case IndexExpression index:
                VisitExpression(index.Target, inParallelTask, visitCall);
                VisitExpression(index.Index, inParallelTask, visitCall);
                return;
            case MemberAccessExpression member:
                VisitExpression(member.Target, inParallelTask, visitCall);
                return;
            case ConditionalMemberAccessExpression member:
                VisitExpression(member.Target, inParallelTask, visitCall);
                return;
            case ParallelExpressionSyntax parallel:
                foreach (var task in parallel.Tasks)
                {
                    VisitStatements(task.Body, true, visitCall);
                }
                return;
        }
    }

    private static void AnalyzeStatement(
        Statement statement,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ISet<string> declarations,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver,
        FunctionReturnContext? returnContext = null)
    {
        switch (statement)
        {
            case VarStatement variable:
                var variableType = AnalyzeExpression(variable.Initializer, scope, globals, diagnostics, functionResolver);

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

            case ConstStatement constant:
                var constantType = AnalyzeExpression(constant.Initializer, scope, globals, diagnostics, functionResolver);
                if (!declarations.Add(constant.Name))
                {
                    diagnostics.Add(Create(
                        RuleScriptDiagnosticCodes.DuplicateDeclaration,
                        RuleScriptDiagnosticSeverity.Error,
                        $"Constant '{constant.Name}' is declared more than once in the same scope.",
                        constant.Line,
                        constant.Column,
                        constant.Name,
                        constant.SourceSpan));
                }
                scope[constant.Name] = constantType;
                break;

            case DestructuringVarStatement destructuring:
                AnalyzeDestructuringStatement(
                    destructuring,
                    scope,
                    globals,
                    declarations,
                    diagnostics,
                    functionResolver);
                break;

            case AssignmentStatement assignment:
                var assignedType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, functionResolver);
                ReportAssignmentMismatch(assignment.Name, assignedType, scope, assignment.Line, assignment.Column, assignment.SourceSpan, diagnostics);
                scope[assignment.Name] = assignedType;
                break;

            case TargetAssignmentStatement assignment:
                var assignedTargetType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, functionResolver);
                AnalyzeAssignmentTarget(
                    assignment.Target,
                    assignedTargetType,
                    scope,
                    globals,
                    diagnostics,
                    functionResolver,
                    assignment.SourceSpan);
                break;

            case GlobalAssignmentStatement assignment:
                var assignedGlobalType = AnalyzeExpression(assignment.Value, scope, globals, diagnostics, functionResolver);
                ReportAssignmentMismatch(assignment.Name, assignedGlobalType, globals, assignment.Line, assignment.Column, assignment.SourceSpan, diagnostics);
                globals[assignment.Name] = assignedGlobalType;
                break;

            case ExpressionStatement expression:
                AnalyzeExpression(expression.Expression, scope, globals, diagnostics, functionResolver);
                break;

            case ReturnStatement returnStatement:
                var returnType = AnalyzeExpression(returnStatement.Value, scope, globals, diagnostics, functionResolver);
                if (returnContext is { } context)
                {
                    ValidateReturnType(context, returnStatement, returnType, diagnostics);
                }
                break;

            case IfStatement conditional:
                RequireType(
                    AnalyzeExpression(conditional.Condition, scope, globals, diagnostics, functionResolver),
                    RuleScriptValueType.Boolean,
                    "If condition",
                    conditional.Line,
                    conditional.Column,
                    "if",
                    conditional.SourceSpan,
                    diagnostics);
                AnalyzeChildren(conditional.ThenBranch, scope, globals, declarations, diagnostics, functionResolver, returnContext);
                AnalyzeChildren(conditional.ElseBranch, scope, globals, declarations, diagnostics, functionResolver, returnContext);
                break;

            case SwitchStatement switchStatement:
                var switchType = AnalyzeExpression(switchStatement.Value, scope, globals, diagnostics, functionResolver);
                var constants = new Dictionary<SwitchConstant, bool>();
                var analyzedGuards = new HashSet<Expression>(ReferenceEqualityComparer.Instance);

                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var label in switchCase.Labels)
                    {
                        var labelType = AnalyzeExpression(label.Value, scope, globals, diagnostics, functionResolver);

                        if (label.Guard is not null && analyzedGuards.Add(label.Guard))
                        {
                            RequireType(
                                AnalyzeExpression(label.Guard, scope, globals, diagnostics, functionResolver),
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

                    AnalyzeChildren(switchCase.Body, scope, globals, declarations, diagnostics, functionResolver, returnContext);
                }

                if (switchStatement.DefaultBranch is not null)
                {
                    AnalyzeChildren(switchStatement.DefaultBranch, scope, globals, declarations, diagnostics, functionResolver, returnContext);
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
                    AnalyzeExpression(loop.Condition, scope, globals, diagnostics, functionResolver),
                    RuleScriptValueType.Boolean,
                    "While condition",
                    loop.Line,
                    loop.Column,
                    "while",
                    loop.SourceSpan,
                    diagnostics);
                AnalyzeChildren(loop.Body, scope, globals, declarations, diagnostics, functionResolver, returnContext);
                break;

            case ForeachStatement loop:
                var iterableType = AnalyzeExpression(loop.Iterable, scope, globals, diagnostics, functionResolver);

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
                AnalyzeChildren(loop.Body, scope, globals, declarations, diagnostics, functionResolver, returnContext);

                if (hadPrevious)
                {
                    scope[loop.VariableName] = previousType!;
                }
                else
                {
                    scope.Remove(loop.VariableName);
                }

                break;

            case ParallelStatementSyntax parallel:
                AnalyzeParallelTasks(parallel.Tasks, scope, globals, diagnostics, functionResolver);
                break;
        }
    }

    private static IReadOnlyList<RuleScriptTypeInfo> AnalyzeParallelTasks(
        IReadOnlyList<TaskBlockSyntax> tasks,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver)
    {
        var returnTypes = new List<RuleScriptTypeInfo>(tasks.Count);
        var previous = AnalyzingParallelTask.Value;
        AnalyzingParallelTask.Value = true;
        try
        {
            foreach (var task in tasks)
            {
                var taskScope = new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal);
                var declarations = new HashSet<string>(StringComparer.Ordinal);
                var returns = task.Body.OfType<ReturnStatement>().ToArray();
                foreach (var statement in task.Body)
                {
                    AnalyzeStatement(statement, taskScope, globals, declarations, diagnostics, functionResolver);
                }

                returnTypes.Add(returns.Length == 0
                    ? RuleScriptTypeInfo.From(RuleScriptValueType.Null)
                    : AnalyzeExpression(returns[0].Value, taskScope, globals, diagnostics, functionResolver));
            }
        }
        finally
        {
            AnalyzingParallelTask.Value = previous;
        }

        var knownKinds = returnTypes
            .Select(type => type.Kind)
            .Where(kind => kind is not RuleScriptValueType.Unknown and not RuleScriptValueType.Any and not RuleScriptValueType.Null)
            .Distinct()
            .ToArray();
        if (knownKinds.Length > 1)
        {
            var first = tasks[0];
            diagnostics.Add(new RuleScriptDiagnostic(
                "Parallel tasks return different value types; the expression type is array<any>.",
                first.Line,
                first.Column,
                "parallel")
            {
                Code = RuleScriptDiagnosticCodes.ParallelReturnTypeMismatch,
                Severity = RuleScriptDiagnosticSeverity.Warning
            });
        }

        return returnTypes;
    }

    private static void AnalyzeDestructuringStatement(
        DestructuringVarStatement statement,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ISet<string> declarations,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver)
    {
        var initializerType = AnalyzeExpression(
            statement.Initializer,
            scope,
            globals,
            diagnostics,
            functionResolver);
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

    private static void ReportReadonlyAssignments(
        IEnumerable<Statement> statements,
        IReadOnlySet<string> readonlyNames,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            string? assignedName = statement switch
            {
                AssignmentStatement assignment => assignment.Name,
                TargetAssignmentStatement { Target: IdentifierExpression identifier } => identifier.Name,
                _ => null
            };

            if (assignedName is not null && readonlyNames.Contains(assignedName))
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.CannotAssignToReadonly,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Cannot assign to readonly constant '{assignedName}'.",
                    statement switch
                    {
                        AssignmentStatement assignment => assignment.Line,
                        TargetAssignmentStatement assignment => assignment.Line,
                        _ => null
                    },
                    statement switch
                    {
                        AssignmentStatement assignment => assignment.Column,
                        TargetAssignmentStatement assignment => assignment.Column,
                        _ => null
                    },
                    assignedName,
                    statement.SourceSpan));
            }

            switch (statement)
            {
                case FunctionDeclarationStatement function:
                    ReportReadonlyAssignments(function.Body, readonlyNames, diagnostics);
                    break;
                case IfStatement conditional:
                    ReportReadonlyAssignments(conditional.ThenBranch, readonlyNames, diagnostics);
                    ReportReadonlyAssignments(conditional.ElseBranch, readonlyNames, diagnostics);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        ReportReadonlyAssignments(switchCase.Body, readonlyNames, diagnostics);
                    }
                    if (switchStatement.DefaultBranch is not null)
                    {
                        ReportReadonlyAssignments(switchStatement.DefaultBranch, readonlyNames, diagnostics);
                    }
                    break;
                case WhileStatement loop:
                    ReportReadonlyAssignments(loop.Body, readonlyNames, diagnostics);
                    break;
                case ForeachStatement loop:
                    ReportReadonlyAssignments(loop.Body, readonlyNames, diagnostics);
                    break;
            }
        }
    }

    private static void AnalyzeAssignmentTarget(
        Expression target,
        RuleScriptTypeInfo assignedType,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver,
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
                var receiverType = AnalyzeExpression(member.Target, scope, globals, diagnostics, functionResolver);

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
                var receiver = AnalyzeExpression(index.Target, scope, globals, diagnostics, functionResolver);
                var indexType = AnalyzeExpression(index.Index, scope, globals, diagnostics, functionResolver);

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
                AnalyzeExpression(target, scope, globals, diagnostics, functionResolver);
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
        IRuleScriptFunctionResolver functionResolver,
        FunctionReturnContext? returnContext = null)
    {
        foreach (var statement in statements)
        {
            AnalyzeStatement(statement, scope, globals, declarations, diagnostics, functionResolver, returnContext);
        }
    }

    private static void ValidateReturnType(
        FunctionReturnContext context,
        ReturnStatement statement,
        RuleScriptTypeInfo actual,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        if (context.DeclaredReturnType == RuleScriptValueType.Void)
        {
            if (statement.Value is not null)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.TypeMismatch,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Function '{context.FunctionName}' declares return type void and cannot return a value.",
                    statement.Line,
                    statement.Column,
                    "return",
                    statement.SourceSpan));
            }

            return;
        }

        if (!IsKnown(actual) || actual.Kind == RuleScriptValueType.Any)
        {
            return;
        }

        if (actual.Kind != context.DeclaredReturnType)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.TypeMismatch,
                RuleScriptDiagnosticSeverity.Error,
                $"Function '{context.FunctionName}' declares return type {RuleScriptTypeFacts.ToDisplayName(context.DeclaredReturnType)}, but returns {RuleScriptTypeFacts.ToDisplayName(actual.Kind)}.",
                statement.Line,
                statement.Column,
                "return",
                statement.SourceSpan));
        }
    }

    private static FunctionReturnShape AnalyzeFunctionReturnShape(IReadOnlyList<Statement> statements)
    {
        var hasValueReturn = false;
        var alwaysReturns = false;

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatement returned:
                    hasValueReturn |= returned.Value is not null;
                    alwaysReturns = true;
                    break;
                case IfStatement conditional:
                    var thenShape = AnalyzeFunctionReturnShape(conditional.ThenBranch);
                    var elseShape = AnalyzeFunctionReturnShape(conditional.ElseBranch);
                    hasValueReturn |= thenShape.HasValueReturn || elseShape.HasValueReturn;
                    alwaysReturns = conditional.ElseBranch.Count > 0
                        && thenShape.AlwaysReturns
                        && elseShape.AlwaysReturns;
                    break;
                case SwitchStatement switchStatement:
                    var switchAlwaysReturns = switchStatement.DefaultBranch is not null;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        var caseShape = AnalyzeFunctionReturnShape(switchCase.Body);
                        hasValueReturn |= caseShape.HasValueReturn;
                        switchAlwaysReturns &= caseShape.AlwaysReturns;
                    }

                    if (switchStatement.DefaultBranch is not null)
                    {
                        var defaultShape = AnalyzeFunctionReturnShape(switchStatement.DefaultBranch);
                        hasValueReturn |= defaultShape.HasValueReturn;
                        switchAlwaysReturns &= defaultShape.AlwaysReturns;
                    }

                    alwaysReturns = switchAlwaysReturns;
                    break;
                case WhileStatement loop:
                    var whileShape = AnalyzeFunctionReturnShape(loop.Body);
                    hasValueReturn |= whileShape.HasValueReturn;
                    break;
                case ForeachStatement loop:
                    var foreachShape = AnalyzeFunctionReturnShape(loop.Body);
                    hasValueReturn |= foreachShape.HasValueReturn;
                    break;
            }

            if (alwaysReturns)
            {
                break;
            }
        }

        return new FunctionReturnShape(hasValueReturn, alwaysReturns);
    }

    private static bool TryGetDeclaredReturnType(
        FunctionDeclarationStatement function,
        out RuleScriptValueType returnType)
    {
        if (function.ReturnTypeName is not null && RuleScriptTypeFacts.TryParse(function.ReturnTypeName, out returnType))
        {
            return true;
        }

        returnType = RuleScriptValueType.Unknown;
        return false;
    }

    private static IReadOnlyList<RuleScriptParameterSymbol> CreateParameterSymbols(FunctionDeclarationStatement function)
    {
        return function.ParameterDefinitions.Select(parameter =>
        {
            var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                ? parsed
                : RuleScriptValueType.Unknown;
            return new RuleScriptParameterSymbol(parameter.Name, type);
        }).ToArray();
    }

    private static RuleScriptTypeInfo AnalyzeExpression(
        Expression? expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver)
    {
        switch (expression)
        {
            case null:
                return RuleScriptTypeInfo.From(RuleScriptValueType.Null);
            case LiteralExpression literal:
                return RuleScriptTypeInfo.FromValue(literal.Value);
            case ArrayExpression array:
                return RuleScriptTypeInfo.CreateArray(array.Elements.Select(element =>
                    AnalyzeExpression(element, scope, globals, diagnostics, functionResolver)));
            case ObjectLiteralExpression objectLiteral:
                var properties = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);

                foreach (var property in objectLiteral.Properties)
                {
                    var inferredPropertyType = AnalyzeExpression(property.Value, scope, globals, diagnostics, functionResolver);

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
                var operandType = AnalyzeExpression(unary.Operand, scope, globals, diagnostics, functionResolver);
                var expectedUnaryType = unary.Operator == TokenType.Bang ? RuleScriptValueType.Boolean : RuleScriptValueType.Number;
                RequireType(operandType, expectedUnaryType, $"Operator '{unary.TokenText}'", unary.Line, unary.Column, unary.TokenText, null, diagnostics);
                return RuleScriptTypeInfo.From(expectedUnaryType);
            case BinaryExpression binary:
                return AnalyzeBinary(binary, scope, globals, diagnostics, functionResolver);
            case NullCoalescingExpression coalescing:
                return AnalyzeNullCoalescing(coalescing, scope, globals, diagnostics, functionResolver);
            case FunctionCallExpression call:
                return AnalyzeFunctionCall(call.Name, call.Arguments, call.Line, call.Column, scope, globals, diagnostics, functionResolver);
            case ModuleFunctionCallExpression call:
                return AnalyzeFunctionCall($"{call.ModuleName}.{call.FunctionName}", call.Arguments, call.Line, call.Column, scope, globals, diagnostics, functionResolver);
            case IndexExpression index:
                var analyzedIndexType = AnalyzeExpression(index.Index, scope, globals, diagnostics, functionResolver);

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

                return AnalyzeExpression(index.Target, scope, globals, diagnostics, functionResolver).ElementType
                    ?? RuleScriptTypeInfo.Unknown;
            case MemberAccessExpression member:
                var targetType = AnalyzeExpression(member.Target, scope, globals, diagnostics, functionResolver);

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
                var conditionalTargetType = AnalyzeExpression(member.Target, scope, globals, diagnostics, functionResolver);

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
            case ParallelExpressionSyntax parallel:
                return RuleScriptTypeInfo.CreateArray(AnalyzeParallelTasks(
                    parallel.Tasks, scope, globals, diagnostics, functionResolver));
            default:
                return RuleScriptTypeInfo.Unknown;
        }
    }

    private static RuleScriptTypeInfo AnalyzeNullCoalescing(
        NullCoalescingExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        ICollection<RuleScriptDiagnostic> diagnostics,
        IRuleScriptFunctionResolver functionResolver)
    {
        var left = AnalyzeExpression(expression.Left, scope, globals, diagnostics, functionResolver);
        var right = AnalyzeExpression(expression.Right, scope, globals, diagnostics, functionResolver);

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
        IRuleScriptFunctionResolver functionResolver)
    {
        var left = AnalyzeExpression(binary.Left, scope, globals, diagnostics, functionResolver);
        var right = AnalyzeExpression(binary.Right, scope, globals, diagnostics, functionResolver);

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
        IRuleScriptFunctionResolver functionResolver)
    {
        var argumentTypes = arguments
            .Select(argument => AnalyzeExpression(argument, scope, globals, diagnostics, functionResolver))
            .ToArray();

        var candidates = functionResolver.ResolveFunctions(name);

        if (candidates.Count == 0)
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

        var function = ResolveOverload(name, candidates, argumentTypes, line, column, diagnostics);
        if (function is null)
        {
            return RuleScriptTypeInfo.Unknown;
        }

        if (function.Kind is RuleScriptFunctionKind.User or RuleScriptFunctionKind.Imported)
        {
            ValidateArguments(name, argumentTypes, function.Parameters, line, column, diagnostics);
            var returnType = RuleScriptTypeInfo.From(function.ReturnType);
            return function.IsReturnTypeNullable ? returnType.MakeNullable() : returnType;
        }

        if (function.Kind is RuleScriptFunctionKind.Host or RuleScriptFunctionKind.Builtin)
        {
            if (function.HostMetadata?.IsVariadic != true && function.HostMetadata is not null)
            {
                ValidateArguments(name, argumentTypes, function.Parameters, line, column, diagnostics);
            }
            if (AnalyzingParallelTask.Value && function.HostMetadata?.IsThreadSafe != true)
            {
                diagnostics.Add(Create(
                    RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe,
                    RuleScriptDiagnosticSeverity.Error,
                    $"Host function '{name}' is not marked thread-safe and cannot be called from a parallel task.",
                    line,
                    column,
                    name));
            }
            return RuleScriptTypeInfo.From(function.ReturnType);
        }

        if (AnalyzingParallelTask.Value)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.HostFunctionNotThreadSafe,
                RuleScriptDiagnosticSeverity.Error,
                $"Host function '{name}' is not marked thread-safe and cannot be called from a parallel task.",
                line,
                column,
                name));
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

    private static RuleScriptFunctionSymbol? ResolveOverload(
        string name,
        IReadOnlyList<RuleScriptFunctionSymbol> candidates,
        IReadOnlyList<RuleScriptTypeInfo> arguments,
        int? line,
        int? column,
        ICollection<RuleScriptDiagnostic> diagnostics)
    {
        var matches = candidates
            .Select(candidate => new
            {
                Function = candidate,
                Score = GetOverloadScore(candidate, arguments)
            })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        if (matches.Length == 0)
        {
            foreach (var candidate in candidates.Where(candidate => candidate.Parameters.Count == arguments.Count))
            {
                ValidateArguments(name, arguments, candidate.Parameters, line, column, diagnostics);
            }

            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.UndefinedFunction,
                RuleScriptDiagnosticSeverity.Error,
                $"No matching overload for function '{name}'.",
                line,
                column,
                name));
            return null;
        }

        var bestScore = matches[0].Score;
        var bestMatches = matches.Where(match => match.Score == bestScore).ToArray();
        if (bestMatches.Length > 1)
        {
            diagnostics.Add(Create(
                RuleScriptDiagnosticCodes.TypeMismatch,
                RuleScriptDiagnosticSeverity.Error,
                $"Ambiguous overload for function '{name}'.",
                line,
                column,
                name));
            return null;
        }

        return bestMatches[0].Function;
    }

    private static int GetOverloadScore(
        RuleScriptFunctionSymbol function,
        IReadOnlyList<RuleScriptTypeInfo> arguments)
    {
        if (function.HostMetadata?.IsVariadic == true)
        {
            return 0;
        }

        if (function.HostMetadata is null
            && function.Kind == RuleScriptFunctionKind.Host
            && function.Parameters.Count == 0)
        {
            return 0;
        }

        if (function.Parameters.Count != arguments.Count)
        {
            return -1;
        }

        var score = 0;
        for (var index = 0; index < arguments.Count; index++)
        {
            var expected = function.Parameters[index].Type;
            var actual = arguments[index];

            if (expected is RuleScriptValueType.Any or RuleScriptValueType.Unknown)
            {
                continue;
            }

            if (!IsKnown(actual) || actual.Kind == RuleScriptValueType.Any)
            {
                score++;
                continue;
            }

            if (expected != actual.Kind)
            {
                return -1;
            }

            score += 2;
        }

        return score;
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
        var range = RuleScriptSourceMapper.CreateRange(null, span)
            ?? RuleScriptSourceMapper.CreateTokenRange(null, line, column, tokenText);

        return new RuleScriptDiagnostic(message, line, column, tokenText, null, range)
        {
            Code = code,
            Severity = severity
        };
    }
}
