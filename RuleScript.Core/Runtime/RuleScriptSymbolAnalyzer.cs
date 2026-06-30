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
        var globals = new Dictionary<string, RuleScriptValueType>(knownVariables, StringComparer.Ordinal);
        var allVariables = new Dictionary<string, RuleScriptValueType>(knownVariables, StringComparer.Ordinal);
        var functions = new List<RuleScriptFunctionSymbol>();
        Dictionary<string, RuleScriptValueType>? cursorLocals = null;

        foreach (var statement in statements.Where(statement => statement is not FunctionDeclarationStatement))
        {
            CollectStatement(statement, globals, globals, allVariables, hostFunctionReturnTypes);
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

            var locals = new Dictionary<string, RuleScriptValueType>(StringComparer.Ordinal);

            foreach (var parameter in parameters)
            {
                SetType(locals, parameter.Name, parameter.Type);
                SetType(allVariables, parameter.Name, parameter.Type);
            }

            foreach (var statement in function.Body)
            {
                CollectStatement(statement, locals, globals, allVariables, hostFunctionReturnTypes);
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
            var visibleTypes = new Dictionary<string, RuleScriptValueType>(globals, StringComparer.Ordinal);

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
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        IDictionary<string, RuleScriptValueType> allVariables,
        IReadOnlyDictionary<string, RuleScriptValueType> hostFunctionReturnTypes)
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
                SetVariable(scope, allVariables, loop.VariableName, RuleScriptValueType.Unknown);
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
            case WhileStatement loop:
                foreach (var child in loop.Body)
                {
                    CollectStatement(child, scope, globals, allVariables, hostFunctionReturnTypes);
                }

                break;
        }
    }

    private static RuleScriptValueType Infer(
        Expression? expression,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        IReadOnlyDictionary<string, RuleScriptValueType> hostFunctionReturnTypes)
    {
        return expression switch
        {
            null => RuleScriptValueType.Null,
            LiteralExpression literal => RuleScriptTypeFacts.FromValue(literal.Value),
            ArrayExpression => RuleScriptValueType.Array,
            IdentifierExpression identifier => Lookup(identifier.Name, scope, globals),
            GlobalIdentifierExpression identifier => GetType(globals, identifier.Name),
            UnaryExpression { Operator: TokenType.Bang } => RuleScriptValueType.Boolean,
            UnaryExpression { Operator: TokenType.Minus } => RuleScriptValueType.Number,
            BinaryExpression binary => InferBinary(binary, scope, globals, hostFunctionReturnTypes),
            FunctionCallExpression call => InferFunction(call.Name, hostFunctionReturnTypes),
            _ => RuleScriptValueType.Unknown
        };
    }

    private static RuleScriptValueType InferBinary(
        BinaryExpression expression,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals,
        IReadOnlyDictionary<string, RuleScriptValueType> hostFunctionReturnTypes)
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
            return RuleScriptValueType.Boolean;
        }

        if (expression.Operator == TokenType.Plus)
        {
            var left = Infer(expression.Left, scope, globals, hostFunctionReturnTypes);
            var right = Infer(expression.Right, scope, globals, hostFunctionReturnTypes);

            if (left == RuleScriptValueType.String || right == RuleScriptValueType.String)
            {
                return RuleScriptValueType.String;
            }
        }

        return expression.Operator is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            ? RuleScriptValueType.Number
            : RuleScriptValueType.Unknown;
    }

    private static RuleScriptValueType InferFunction(
        string name,
        IReadOnlyDictionary<string, RuleScriptValueType> hostFunctionReturnTypes)
    {
        if (hostFunctionReturnTypes.TryGetValue(name, out var hostReturnType))
        {
            return hostReturnType;
        }

        return name switch
        {
            "ToString" or "Concat" or "Replace" or "Trim" or "ToUpper" or "ToLower" or "Substring" or "Join" or "JsonStringify" => RuleScriptValueType.String,
            "ParseInt" or "ParseDecimal" or "Length" or "IndexOf" or "Abs" or "Round" or "Floor" or "Ceil" or "Min" or "Max" => RuleScriptValueType.Number,
            "ParseBool" or "Contains" or "StartsWith" or "EndsWith" or "JsonExists" => RuleScriptValueType.Boolean,
            "Split" or "JsonKeys" => RuleScriptValueType.Array,
            "JsonParse" => RuleScriptValueType.Object,
            _ => RuleScriptValueType.Unknown
        };
    }

    private static RuleScriptValueType Lookup(
        string name,
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> globals)
    {
        return scope.TryGetValue(name, out var type)
            ? type
            : GetType(globals, name);
    }

    private static RuleScriptValueType GetType(
        IDictionary<string, RuleScriptValueType> values,
        string name)
    {
        return values.TryGetValue(name, out var type) ? type : RuleScriptValueType.Unknown;
    }

    private static void SetVariable(
        IDictionary<string, RuleScriptValueType> scope,
        IDictionary<string, RuleScriptValueType> allVariables,
        string name,
        RuleScriptValueType type)
    {
        SetType(scope, name, type);
        SetType(allVariables, name, type);
    }

    private static void SetType(IDictionary<string, RuleScriptValueType> values, string name, RuleScriptValueType type)
    {
        if (!values.TryGetValue(name, out var existing) || existing == RuleScriptValueType.Unknown)
        {
            values[name] = type;
            return;
        }

        if (type != RuleScriptValueType.Unknown && existing != type)
        {
            values[name] = RuleScriptValueType.Unknown;
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
        IDictionary<string, RuleScriptValueType> values)
    {
        return values
            .Select(value => new RuleScriptVariableSymbol(value.Key, value.Value))
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
