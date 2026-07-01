using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed record RuleScriptTypedSymbols(
    IReadOnlyList<RuleScriptVariableSymbol> Variables,
    IReadOnlyList<RuleScriptFunctionSymbol> Functions,
    IReadOnlyList<RuleScriptVariableSymbol>? VisibleVariables,
    IReadOnlyList<RuleScriptDiagnostic> Diagnostics);

internal sealed record RuleScriptFunctionReturnAnalysis(
    RuleScriptTypeInfo Type,
    bool HasIncompatibleReturns);

internal sealed record RuleScriptReturnFlow(
    IReadOnlyList<RuleScriptTypeInfo> ReturnTypes,
    bool AlwaysReturns);

internal static class RuleScriptSymbolAnalyzer
{
    public static RuleScriptTypedSymbols Analyze(
        IReadOnlyList<Statement> statements,
        int? cursorLine,
        int? cursorColumn,
        IReadOnlyDictionary<string, RuleScriptValueType>? hostFunctionReturnTypes = null,
        IReadOnlyDictionary<string, RuleScriptValueType>? knownVariables = null,
        IReadOnlyDictionary<string, RuleScriptTypeInfo>? knownTypeInfos = null)
    {
        hostFunctionReturnTypes ??= new Dictionary<string, RuleScriptValueType>(StringComparer.Ordinal);
        knownVariables ??= new Dictionary<string, RuleScriptValueType>(StringComparer.Ordinal);
        var hostReturnTypes = hostFunctionReturnTypes.ToDictionary(
            value => value.Key,
            value => RuleScriptTypeInfo.From(value.Value),
            StringComparer.Ordinal);
        var knownTypes = knownVariables.ToDictionary(
            value => value.Key,
            value => RuleScriptTypeInfo.From(value.Value),
            StringComparer.Ordinal);
        if (knownTypeInfos is not null)
        {
            foreach (var knownType in knownTypeInfos)
            {
                knownTypes[knownType.Key] = knownType.Value;
            }
        }
        var globals = new Dictionary<string, RuleScriptTypeInfo>(knownTypes, StringComparer.Ordinal);
        var allVariables = new Dictionary<string, RuleScriptTypeInfo>(knownTypes, StringComparer.Ordinal);
        var functionDeclarations = statements
            .OfType<FunctionDeclarationStatement>()
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var functionParameters = functionDeclarations.ToDictionary(
            function => function.Name,
            function => function.ParameterDefinitions.Select(parameter =>
            {
                var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                    ? parsed
                    : RuleScriptValueType.Unknown;
                return new RuleScriptParameterSymbol(parameter.Name, type);
            }).ToArray(),
            StringComparer.Ordinal);
        var functionReturnTypes = functionDeclarations.ToDictionary(
            function => function.Name,
            _ => RuleScriptTypeInfo.Unknown,
            StringComparer.Ordinal);
        Dictionary<string, RuleScriptTypeInfo>? cursorLocals = null;

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            CollectStatement(statement, globals, globals, allVariables, hostReturnTypes);
        }

        for (var iteration = 0; iteration <= functionDeclarations.Length; iteration++)
        {
            var changed = false;
            var callableReturnTypes = new Dictionary<string, RuleScriptTypeInfo>(hostReturnTypes, StringComparer.Ordinal);
            foreach (var returnType in functionReturnTypes)
            {
                callableReturnTypes[returnType.Key] = returnType.Value;
            }

            foreach (var function in functionDeclarations)
            {
                var analysis = InferFunctionReturnType(
                    function,
                    functionParameters[function.Name],
                    globals,
                    callableReturnTypes);
                if (!TypesEquivalent(functionReturnTypes[function.Name], analysis.Type))
                {
                    functionReturnTypes[function.Name] = analysis.Type;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var finalCallableReturnTypes = new Dictionary<string, RuleScriptTypeInfo>(hostReturnTypes, StringComparer.Ordinal);
        foreach (var returnType in functionReturnTypes)
        {
            finalCallableReturnTypes[returnType.Key] = returnType.Value;
        }

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            CollectStatement(statement, globals, globals, allVariables, finalCallableReturnTypes);
        }

        var functions = functionDeclarations.Select(function =>
        {
            var returnType = functionReturnTypes[function.Name];
            return new RuleScriptFunctionSymbol(
                function.Name,
                functionParameters[function.Name],
                returnType.Kind,
                returnType.IsNullable,
                function.IsExported);
        }).ToList();
        var diagnostics = new List<RuleScriptDiagnostic>();

        foreach (var function in functionDeclarations)
        {
            var parameters = functionParameters[function.Name];
            var finalAnalysis = InferFunctionReturnType(function, parameters, globals, finalCallableReturnTypes);
            if (finalAnalysis.HasIncompatibleReturns)
            {
                diagnostics.Add(new RuleScriptDiagnostic(
                    $"Function '{function.Name}' returns incompatible value types.",
                    function.Line,
                    function.Column,
                    function.Name)
                {
                    Code = RuleScriptDiagnosticCodes.TypeMismatch,
                    Severity = RuleScriptDiagnosticSeverity.Error
                });
            }

            var locals = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);

            foreach (var parameter in parameters)
            {
                SetType(locals, parameter.Name, RuleScriptTypeInfo.From(parameter.Type));
                SetType(allVariables, parameter.Name, RuleScriptTypeInfo.From(parameter.Type));
            }

            foreach (var statement in function.Body)
            {
                CollectStatement(statement, locals, globals, allVariables, finalCallableReturnTypes);
            }

            if (cursorLine.HasValue
                && cursorColumn.HasValue
                && Contains(function.SourceSpan, cursorLine.Value, cursorColumn.Value))
            {
                cursorLocals = locals;
            }
        }

        IReadOnlyList<RuleScriptVariableSymbol>? visible = null;

        if (cursorLine.HasValue && cursorColumn.HasValue)
        {
            var visibleTypes = new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal);

            if (cursorLocals is not null)
            {
                foreach (var local in cursorLocals)
                {
                    visibleTypes[local.Key] = local.Value;
                }
            }

            visible = ToSymbols(visibleTypes);
        }

        var constantDeclarations = statements.OfType<ConstStatement>()
            .GroupBy(constant => constant.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var variables = ToSymbols(allVariables)
            .Select(variable => constantDeclarations.TryGetValue(variable.Name, out var constant)
                ? new RuleScriptVariableSymbol(variable.Name, variable.Type, IsReadOnly: true, IsExported: constant.IsExported)
                : knownTypeInfos?.ContainsKey(variable.Name) == true
                    ? new RuleScriptVariableSymbol(variable.Name, variable.Type, IsReadOnly: true, IsExported: true)
                    : variable)
            .ToArray();
        return new RuleScriptTypedSymbols(variables, functions, visible, diagnostics);
    }

    private static RuleScriptFunctionReturnAnalysis InferFunctionReturnType(
        FunctionDeclarationStatement function,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> callableReturnTypes)
    {
        var locals = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            locals[parameter.Name] = RuleScriptTypeInfo.From(parameter.Type);
        }

        var functionGlobals = new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal);
        var flow = AnalyzeReturnBlock(function.Body, locals, functionGlobals, callableReturnTypes);
        return MergeReturnTypes(flow.ReturnTypes, flow.AlwaysReturns);
    }

    private static RuleScriptReturnFlow AnalyzeReturnBlock(
        IReadOnlyList<Statement> statements,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> callableReturnTypes)
    {
        var returnTypes = new List<RuleScriptTypeInfo>();
        var alwaysReturns = false;

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatement returnStatement:
                    returnTypes.Add(Infer(returnStatement.Value, scope, globals, callableReturnTypes));
                    alwaysReturns = true;
                    break;

                case IfStatement conditional:
                    var thenFlow = AnalyzeReturnBlock(
                        conditional.ThenBranch,
                        new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal),
                        new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                        callableReturnTypes);
                    var elseFlow = AnalyzeReturnBlock(
                        conditional.ElseBranch,
                        new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal),
                        new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                        callableReturnTypes);
                    returnTypes.AddRange(thenFlow.ReturnTypes);
                    returnTypes.AddRange(elseFlow.ReturnTypes);
                    alwaysReturns = conditional.ElseBranch.Count > 0
                        && thenFlow.AlwaysReturns
                        && elseFlow.AlwaysReturns;
                    CollectStatement(statement, scope, globals, scope, callableReturnTypes);
                    break;

                case SwitchStatement switchStatement:
                    var switchAlwaysReturns = switchStatement.DefaultBranch is not null;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        var caseFlow = AnalyzeReturnBlock(
                            switchCase.Body,
                            new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal),
                            new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                            callableReturnTypes);
                        returnTypes.AddRange(caseFlow.ReturnTypes);
                        switchAlwaysReturns &= caseFlow.AlwaysReturns;
                    }

                    if (switchStatement.DefaultBranch is not null)
                    {
                        var defaultFlow = AnalyzeReturnBlock(
                            switchStatement.DefaultBranch,
                            new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal),
                            new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                            callableReturnTypes);
                        returnTypes.AddRange(defaultFlow.ReturnTypes);
                        switchAlwaysReturns &= defaultFlow.AlwaysReturns;
                    }

                    alwaysReturns = switchAlwaysReturns;
                    CollectStatement(statement, scope, globals, scope, callableReturnTypes);
                    break;

                case WhileStatement loop:
                    var whileFlow = AnalyzeReturnBlock(
                        loop.Body,
                        new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal),
                        new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                        callableReturnTypes);
                    returnTypes.AddRange(whileFlow.ReturnTypes);
                    CollectStatement(statement, scope, globals, scope, callableReturnTypes);
                    break;

                case ForeachStatement loop:
                    var loopScope = new Dictionary<string, RuleScriptTypeInfo>(scope, StringComparer.Ordinal);
                    loopScope[loop.VariableName] = Infer(loop.Iterable, scope, globals, callableReturnTypes).ElementType
                        ?? RuleScriptTypeInfo.Unknown;
                    var foreachFlow = AnalyzeReturnBlock(
                        loop.Body,
                        loopScope,
                        new Dictionary<string, RuleScriptTypeInfo>(globals, StringComparer.Ordinal),
                        callableReturnTypes);
                    returnTypes.AddRange(foreachFlow.ReturnTypes);
                    CollectStatement(statement, scope, globals, scope, callableReturnTypes);
                    break;

                default:
                    CollectStatement(statement, scope, globals, scope, callableReturnTypes);
                    break;
            }

            if (alwaysReturns)
            {
                break;
            }
        }

        return new RuleScriptReturnFlow(returnTypes, alwaysReturns);
    }

    private static RuleScriptFunctionReturnAnalysis MergeReturnTypes(
        IReadOnlyList<RuleScriptTypeInfo> returnTypes,
        bool alwaysReturns)
    {
        if (returnTypes.Count == 0)
        {
            return new RuleScriptFunctionReturnAnalysis(
                RuleScriptTypeInfo.From(RuleScriptValueType.Null),
                false);
        }

        var nullable = !alwaysReturns || returnTypes.Any(type => type.Kind == RuleScriptValueType.Null || type.IsNullable);
        var knownTypes = returnTypes
            .Select(type => type.WithoutNull())
            .Where(type => type.Kind is not RuleScriptValueType.Null and not RuleScriptValueType.Unknown)
            .ToArray();

        if (knownTypes.Length == 0)
        {
            var type = returnTypes.All(value => value.Kind == RuleScriptValueType.Null)
                ? RuleScriptTypeInfo.From(RuleScriptValueType.Null)
                : RuleScriptTypeInfo.Unknown;
            return new RuleScriptFunctionReturnAnalysis(type, false);
        }

        if (knownTypes.Any(type => type.Kind == RuleScriptValueType.Any))
        {
            var anyType = RuleScriptTypeInfo.From(RuleScriptValueType.Any);
            return new RuleScriptFunctionReturnAnalysis(nullable ? anyType.MakeNullable() : anyType, false);
        }

        var distinctKinds = knownTypes.Select(type => type.Kind).Distinct().ToArray();
        if (distinctKinds.Length > 1)
        {
            return new RuleScriptFunctionReturnAnalysis(RuleScriptTypeInfo.Unknown, true);
        }

        var inferred = knownTypes[0];
        return new RuleScriptFunctionReturnAnalysis(nullable ? inferred.MakeNullable() : inferred, false);
    }

    private static bool TypesEquivalent(RuleScriptTypeInfo left, RuleScriptTypeInfo right)
    {
        return left.Kind == right.Kind && left.IsNullable == right.IsNullable;
    }

    private static void CollectStatement(
        Statement statement,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IDictionary<string, RuleScriptTypeInfo> allVariables,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        switch (statement)
        {
            case VarStatement variable:
                SetVariable(scope, allVariables, variable.Name, Infer(variable.Initializer, scope, globals, hostFunctionReturnTypes));
                break;
            case ConstStatement constant:
                SetVariable(scope, allVariables, constant.Name, Infer(constant.Initializer, scope, globals, hostFunctionReturnTypes));
                break;
            case DestructuringVarStatement destructuring:
                CollectDestructuringStatement(destructuring, scope, globals, allVariables, hostFunctionReturnTypes);
                break;
            case AssignmentStatement assignment:
                SetVariable(scope, allVariables, assignment.Name, Infer(assignment.Value, scope, globals, hostFunctionReturnTypes));
                break;
            case TargetAssignmentStatement { Target: IdentifierExpression identifier } assignment:
                SetVariable(scope, allVariables, identifier.Name, Infer(assignment.Value, scope, globals, hostFunctionReturnTypes));
                break;
            case TargetAssignmentStatement { Target: GlobalIdentifierExpression identifier } assignment:
                var assignedGlobalType = Infer(assignment.Value, scope, globals, hostFunctionReturnTypes);
                SetType(globals, identifier.Name, assignedGlobalType);
                SetType(allVariables, identifier.Name, assignedGlobalType);
                break;
            case GlobalAssignmentStatement assignment:
                var globalType = Infer(assignment.Value, scope, globals, hostFunctionReturnTypes);
                SetType(globals, assignment.Name, globalType);
                SetType(allVariables, assignment.Name, globalType);
                break;
            case ForeachStatement loop:
                SetVariable(scope, allVariables, loop.VariableName, RuleScriptTypeInfo.Unknown);
                foreach (var child in loop.Body)
                {
                    CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                }

                break;
            case IfStatement conditional:
                foreach (var child in conditional.ThenBranch)
                {
                    CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                }

                foreach (var child in conditional.ElseBranch)
                {
                    CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                }

                break;
            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var child in switchCase.Body)
                    {
                        CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                    }
                }

                if (switchStatement.DefaultBranch is not null)
                {
                    foreach (var child in switchStatement.DefaultBranch)
                    {
                        CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                    }
                }

                break;
            case WhileStatement loop:
                foreach (var child in loop.Body)
                {
                    CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                }

                break;
        }
    }

    private static void CollectDestructuringStatement(
        DestructuringVarStatement statement,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IDictionary<string, RuleScriptTypeInfo> allVariables,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        var initializerType = Infer(statement.Initializer, scope, globals, hostFunctionReturnTypes);

        for (var index = 0; index < statement.Pattern.Names.Count; index++)
        {
            var name = statement.Pattern.Names[index];
            var type = statement.Pattern switch
            {
                ArrayDestructuringPattern when initializerType.ElementTypes is not null && index < initializerType.ElementTypes.Count
                    => initializerType.ElementTypes[index],
                ArrayDestructuringPattern => initializerType.ElementType ?? RuleScriptTypeInfo.Unknown,
                ObjectDestructuringPattern when initializerType.TryGetProperty(name, out var propertyType) => propertyType,
                _ => RuleScriptTypeInfo.Unknown
            };
            SetVariable(scope, allVariables, name, type);
        }
    }

    private static RuleScriptTypeInfo Infer(
        Expression? expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        return expression switch
        {
            null => RuleScriptTypeInfo.From(RuleScriptValueType.Null),
            LiteralExpression literal => RuleScriptTypeInfo.FromValue(literal.Value),
            ArrayExpression array => RuleScriptTypeInfo.CreateArray(
                array.Elements.Select(element => Infer(element, scope, globals, hostFunctionReturnTypes))),
            ObjectLiteralExpression objectLiteral => InferObjectLiteral(objectLiteral, scope, globals, hostFunctionReturnTypes),
            IdentifierExpression identifier => Lookup(identifier.Name, scope, globals),
            GlobalIdentifierExpression identifier => GetType(globals, identifier.Name),
            UnaryExpression { Operator: TokenType.Bang } => RuleScriptTypeInfo.From(RuleScriptValueType.Boolean),
            UnaryExpression { Operator: TokenType.Minus } => RuleScriptTypeInfo.From(RuleScriptValueType.Number),
            BinaryExpression binary => InferBinary(binary, scope, globals, hostFunctionReturnTypes),
            NullCoalescingExpression coalescing => InferNullCoalescing(coalescing, scope, globals, hostFunctionReturnTypes),
            FunctionCallExpression call => InferFunction(call, scope, globals, hostFunctionReturnTypes),
            IndexExpression index => Infer(index.Target, scope, globals, hostFunctionReturnTypes).ElementType ?? RuleScriptTypeInfo.Unknown,
            MemberAccessExpression member => InferMemberAccess(member, scope, globals, hostFunctionReturnTypes),
            ConditionalMemberAccessExpression member => InferConditionalMemberAccess(member, scope, globals, hostFunctionReturnTypes),
            _ => RuleScriptTypeInfo.Unknown
        };
    }

    private static RuleScriptTypeInfo InferObjectLiteral(
        ObjectLiteralExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        var properties = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);

        foreach (var property in expression.Properties)
        {
            properties[property.Name] = Infer(property.Value, scope, globals, hostFunctionReturnTypes);
        }

        return RuleScriptTypeInfo.CreateObject(properties);
    }

    private static RuleScriptTypeInfo InferMemberAccess(
        MemberAccessExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        var targetType = Infer(expression.Target, scope, globals, hostFunctionReturnTypes);
        return targetType.TryGetProperty(expression.MemberName, out var propertyType)
            ? propertyType
            : RuleScriptTypeInfo.Unknown;
    }

    private static RuleScriptTypeInfo InferConditionalMemberAccess(
        ConditionalMemberAccessExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        var targetType = Infer(expression.Target, scope, globals, hostFunctionReturnTypes);

        if (targetType.Kind == RuleScriptValueType.Null)
        {
            return targetType;
        }

        return targetType.TryGetProperty(expression.MemberName, out var propertyType)
            ? propertyType.MakeNullable()
            : RuleScriptTypeInfo.Unknown.MakeNullable();
    }

    private static RuleScriptTypeInfo InferNullCoalescing(
        NullCoalescingExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        var left = Infer(expression.Left, scope, globals, hostFunctionReturnTypes);
        var right = Infer(expression.Right, scope, globals, hostFunctionReturnTypes);

        if (left.Kind == RuleScriptValueType.Null)
        {
            return right;
        }

        var nonNullLeft = left.WithoutNull();

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

    private static RuleScriptTypeInfo InferBinary(
        BinaryExpression expression,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        if (expression.Operator is TokenType.EqualEqual
            or TokenType.BangEqual
            or TokenType.Greater
            or TokenType.GreaterOrEqual
            or TokenType.Less
            or TokenType.LessOrEqual
            or TokenType.And
            or TokenType.Or)
        {
            return RuleScriptTypeInfo.From(RuleScriptValueType.Boolean);
        }

        if (expression.Operator == TokenType.Plus)
        {
            var left = Infer(expression.Left, scope, globals, hostFunctionReturnTypes);
            var right = Infer(expression.Right, scope, globals, hostFunctionReturnTypes);

            if (left.Kind == RuleScriptValueType.String || right.Kind == RuleScriptValueType.String)
            {
                return RuleScriptTypeInfo.From(RuleScriptValueType.String);
            }
        }

        return expression.Operator is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            ? RuleScriptTypeInfo.From(RuleScriptValueType.Number)
            : RuleScriptTypeInfo.Unknown;
    }

    private static RuleScriptTypeInfo InferFunction(
        FunctionCallExpression call,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        if (call.Name == "ArrayRemoveAt" && call.Arguments.Count > 0)
        {
            return Infer(call.Arguments[0], scope, globals, hostFunctionReturnTypes).ElementType
                ?? RuleScriptTypeInfo.Unknown;
        }

        if (call.Name is "ArrayInsert" or "ArraySort" && call.Arguments.Count > 0)
        {
            return Infer(call.Arguments[0], scope, globals, hostFunctionReturnTypes);
        }

        if (call.Name == "ObjectKeys")
        {
            return RuleScriptTypeInfo.CreateArray([RuleScriptTypeInfo.From(RuleScriptValueType.String)]);
        }

        if (hostFunctionReturnTypes.TryGetValue(call.Name, out var hostReturnType))
        {
            return hostReturnType;
        }

        return call.Name switch
        {
            "ToString" or "Concat" or "Replace" or "Trim" or "ToUpper" or "ToLower" or "Substring" or "Join" or "JsonStringify" => RuleScriptTypeInfo.From(RuleScriptValueType.String),
            "ParseInt" or "ParseDecimal" or "Length" or "IndexOf" or "Abs" or "Round" or "Floor" or "Ceil" or "Min" or "Max" => RuleScriptTypeInfo.From(RuleScriptValueType.Number),
            "ParseBool" or "Contains" or "StartsWith" or "EndsWith" or "JsonExists" => RuleScriptTypeInfo.From(RuleScriptValueType.Boolean),
            "Split" or "JsonKeys" => RuleScriptTypeInfo.From(RuleScriptValueType.Array),
            "JsonParse" => RuleScriptTypeInfo.From(RuleScriptValueType.Object),
            _ => RuleScriptTypeInfo.Unknown
        };
    }

    private static RuleScriptTypeInfo Lookup(
        string name,
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> globals)
    {
        return scope.TryGetValue(name, out var type)
            ? type
            : GetType(globals, name);
    }

    private static RuleScriptTypeInfo GetType(
        IDictionary<string, RuleScriptTypeInfo> values,
        string name)
    {
        return values.TryGetValue(name, out var type) ? type : RuleScriptTypeInfo.Unknown;
    }

    private static void SetVariable(
        IDictionary<string, RuleScriptTypeInfo> scope,
        IDictionary<string, RuleScriptTypeInfo> allVariables,
        string name,
        RuleScriptTypeInfo type)
    {
        SetType(scope, name, type);
        SetType(allVariables, name, type);
    }

    private static void SetType(IDictionary<string, RuleScriptTypeInfo> values, string name, RuleScriptTypeInfo type)
    {
        if (!values.TryGetValue(name, out var existing) || existing.Kind == RuleScriptValueType.Unknown)
        {
            values[name] = type;
            return;
        }

        if (type.Kind != RuleScriptValueType.Unknown && existing.Kind != type.Kind)
        {
            values[name] = RuleScriptTypeInfo.Unknown;
        }
    }

    private static bool Contains(SourceSpan? span, int line, int column)
    {
        if (span is null)
        {
            return false;
        }

        var afterStart = line > span.StartLine || (line == span.StartLine && column >= span.StartColumn);
        var beforeEnd = line < span.EndLine || (line == span.EndLine && column < span.EndColumn);
        return afterStart && beforeEnd;
    }

    private static IReadOnlyList<RuleScriptVariableSymbol> ToSymbols(
        IDictionary<string, RuleScriptTypeInfo> values)
    {
        return values
            .Select(value => new RuleScriptVariableSymbol(value.Key, value.Value.Kind))
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
