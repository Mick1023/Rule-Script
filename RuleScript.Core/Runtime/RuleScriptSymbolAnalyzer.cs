using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed record RuleScriptTypedSymbols(
    IReadOnlyList<RuleScriptVariableSymbol> Variables,
    IReadOnlyList<RuleScriptFunctionSymbol> Functions,
    IReadOnlyList<RuleScriptVariableSymbol>? VisibleVariables);

internal static class RuleScriptSymbolAnalyzer
{
    public static RuleScriptTypedSymbols Analyze(
        IReadOnlyList<Statement> statements,
        int? cursorLine,
        int? cursorColumn,
        IReadOnlyDictionary<string, RuleScriptValueType>? hostFunctionReturnTypes = null,
        IReadOnlyDictionary<string, RuleScriptValueType>? knownVariables = null)
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
        var globals = new Dictionary<string, RuleScriptTypeInfo>(knownTypes, StringComparer.Ordinal);
        var allVariables = new Dictionary<string, RuleScriptTypeInfo>(knownTypes, StringComparer.Ordinal);
        var functions = new List<RuleScriptFunctionSymbol>();
        Dictionary<string, RuleScriptTypeInfo>? cursorLocals = null;

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            CollectStatement(statement, globals, globals, allVariables, hostReturnTypes);
        }

        foreach (var function in statements.OfType<FunctionDeclarationStatement>())
        {
            var parameters = function.ParameterDefinitions.Select(parameter =>
            {
                var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                    ? parsed
                    : RuleScriptValueType.Unknown;
                return new RuleScriptParameterSymbol(parameter.Name, type);
            }).ToArray();
            functions.Add(new RuleScriptFunctionSymbol(function.Name, parameters));

            var locals = new Dictionary<string, RuleScriptTypeInfo>(StringComparer.Ordinal);

            foreach (var parameter in parameters)
            {
                SetType(locals, parameter.Name, RuleScriptTypeInfo.From(parameter.Type));
                SetType(allVariables, parameter.Name, RuleScriptTypeInfo.From(parameter.Type));
            }

            foreach (var statement in function.Body)
            {
                CollectStatement(statement, locals, globals, allVariables, hostReturnTypes);
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

        return new RuleScriptTypedSymbols(ToSymbols(allVariables), functions, visible);
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
            case AssignmentStatement assignment:
                SetVariable(scope, allVariables, assignment.Name, Infer(assignment.Value, scope, globals, hostFunctionReturnTypes));
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
            FunctionCallExpression call => InferFunction(call.Name, hostFunctionReturnTypes),
            IndexExpression index => Infer(index.Target, scope, globals, hostFunctionReturnTypes).ElementType ?? RuleScriptTypeInfo.Unknown,
            MemberAccessExpression member => InferMemberAccess(member, scope, globals, hostFunctionReturnTypes),
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
        string name,
        IReadOnlyDictionary<string, RuleScriptTypeInfo> hostFunctionReturnTypes)
    {
        if (hostFunctionReturnTypes.TryGetValue(name, out var hostReturnType))
        {
            return hostReturnType;
        }

        return name switch
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
