using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;
using System.Collections;
using System.Reflection;

namespace RuleScript.Core.Runtime;

public sealed class Interpreter
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions;
    private readonly int _maxLoopIterations;
    private readonly Dictionary<string, FunctionDeclarationStatement> _userFunctions = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, RuntimeValue>> _localScopes = new();
    private readonly Stack<int> _functionLoopBoundaries = new();
    private readonly Stack<string> _callStack = new();
    private int _loopDepth;

    public Interpreter(BuiltinFunctions builtinFunctions)
        : this(builtinFunctions, new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal), 100000)
    {
    }

    public Interpreter(
        BuiltinFunctions builtinFunctions,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> hostFunctions,
        int maxLoopIterations = 100000)
    {
        _builtinFunctions = builtinFunctions ?? throw new ArgumentNullException(nameof(builtinFunctions));
        _hostFunctions = hostFunctions ?? throw new ArgumentNullException(nameof(hostFunctions));
        _maxLoopIterations = maxLoopIterations > 0
            ? maxLoopIterations
            : throw new ArgumentOutOfRangeException(nameof(maxLoopIterations), "Max loop iterations must be greater than zero.");
    }

    public void Execute(IReadOnlyList<Statement> statements, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var statement in statements)
        {
            if (statement is FunctionDeclarationStatement functionDeclaration)
            {
                _userFunctions[functionDeclaration.Name] = functionDeclaration;
            }
        }

        foreach (var statement in statements)
        {
            if (statement is not FunctionDeclarationStatement)
            {
                ExecuteStatement(statement, context);
            }
        }
    }

    private void ExecuteStatement(Statement statement, RuntimeContext context)
    {
        switch (statement)
        {
            case VarStatement varStatement:
                DeclareVariable(varStatement.Name, varStatement.Initializer is null ? RuntimeValue.Null : Evaluate(varStatement.Initializer, context), context);
                break;
            case AssignmentStatement assignmentStatement:
                AssignVariable(assignmentStatement.Name, Evaluate(assignmentStatement.Value, context), context);
                break;
            case ExpressionStatement expressionStatement:
                Evaluate(expressionStatement.Expression, context);
                break;
            case FunctionDeclarationStatement:
                break;
            case ReturnStatement returnStatement:
                ExecuteReturnStatement(returnStatement, context);
                break;
            case IfStatement ifStatement:
                ExecuteIfStatement(ifStatement, context);
                break;
            case WhileStatement whileStatement:
                ExecuteWhileStatement(whileStatement, context);
                break;
            case ForeachStatement foreachStatement:
                ExecuteForeachStatement(foreachStatement, context);
                break;
            case BreakStatement breakStatement:
                ExecuteBreakStatement(breakStatement);
                break;
            case ContinueStatement continueStatement:
                ExecuteContinueStatement(continueStatement);
                break;
            default:
                throw new RuntimeException($"Unsupported statement type '{statement.GetType().Name}'.");
        }
    }

    private void ExecuteIfStatement(IfStatement statement, RuntimeContext context)
    {
        var condition = Evaluate(statement.Condition, context).Value;

        if (condition is not bool conditionValue)
        {
            throw new RuntimeException("If condition must evaluate to a bool value.", statement.Line, statement.Column, "if");
        }

        var branch = conditionValue ? statement.ThenBranch : statement.ElseBranch;

        foreach (var childStatement in branch)
        {
            ExecuteStatement(childStatement, context);
        }
    }

    private void ExecuteWhileStatement(WhileStatement statement, RuntimeContext context)
    {
        var iterations = 0;
        _loopDepth++;

        try
        {
            while (true)
            {
                if (iterations >= _maxLoopIterations)
                {
                    throw new RuntimeException($"while exceeded max loop iterations limit {_maxLoopIterations}.", statement.Line, statement.Column, "while");
                }

                var condition = Evaluate(statement.Condition, context).Value;

                if (condition is not bool conditionValue)
                {
                    throw new RuntimeException("while condition must evaluate to a bool value.", statement.Line, statement.Column, "while");
                }

                if (!conditionValue)
                {
                    break;
                }

                iterations++;

                try
                {
                    foreach (var childStatement in statement.Body)
                    {
                        ExecuteStatement(childStatement, context);
                    }
                }
                catch (ContinueSignalException)
                {
                    continue;
                }
                catch (BreakSignalException)
                {
                    break;
                }
            }
        }
        finally
        {
            _loopDepth--;
        }
    }

    private void ExecuteForeachStatement(ForeachStatement statement, RuntimeContext context)
    {
        var iterableValue = Evaluate(statement.Iterable, context).Value;
        var items = GetIterableItems(iterableValue, statement);
        var iterations = 0;
        var hasLocalScope = TryGetCurrentLocalScope(out var localScope);
        RuntimeValue? previousRuntimeValue = null;
        object? previousValue = null;
        var hadPreviousValue = hasLocalScope
            ? localScope!.TryGetValue(statement.VariableName, out previousRuntimeValue)
            : context.TryGet(statement.VariableName, out previousValue);

        _loopDepth++;

        try
        {
            foreach (var item in items)
            {
                if (iterations >= _maxLoopIterations)
                {
                    throw new RuntimeException($"foreach exceeded max loop iterations limit {_maxLoopIterations}.", statement.Line, statement.Column, "foreach");
                }

                iterations++;
                if (hasLocalScope)
                {
                    localScope![statement.VariableName] = RuntimeValue.FromObject(item);
                }
                else
                {
                    context.Set(statement.VariableName, item);
                }

                try
                {
                    foreach (var childStatement in statement.Body)
                    {
                        ExecuteStatement(childStatement, context);
                    }
                }
                catch (ContinueSignalException)
                {
                    continue;
                }
                catch (BreakSignalException)
                {
                    break;
                }
            }
        }
        finally
        {
            _loopDepth--;

            if (hasLocalScope)
            {
                if (hadPreviousValue)
                {
                    localScope![statement.VariableName] = previousRuntimeValue!;
                }
                else
                {
                    localScope!.Remove(statement.VariableName);
                }
            }
            else if (hadPreviousValue)
            {
                context.Set(statement.VariableName, previousValue);
            }
            else
            {
                context.Remove(statement.VariableName);
            }
        }
    }

    private void ExecuteBreakStatement(BreakStatement statement)
    {
        if (_loopDepth <= CurrentFunctionLoopBoundary)
        {
            throw new RuntimeException("break can only be used inside a loop.", statement.Line, statement.Column, "break");
        }

        throw new BreakSignalException();
    }

    private void ExecuteContinueStatement(ContinueStatement statement)
    {
        if (_loopDepth <= CurrentFunctionLoopBoundary)
        {
            throw new RuntimeException("continue can only be used inside a loop.", statement.Line, statement.Column, "continue");
        }

        throw new ContinueSignalException();
    }

    private void ExecuteReturnStatement(ReturnStatement statement, RuntimeContext context)
    {
        if (_localScopes.Count == 0)
        {
            throw new RuntimeException("return can only be used inside a function.", statement.Line, statement.Column, "return");
        }

        var value = statement.Value is null ? RuntimeValue.Null : Evaluate(statement.Value, context);
        throw new ReturnSignalException(value);
    }

    private void DeclareVariable(string name, RuntimeValue value, RuntimeContext context)
    {
        if (TryGetCurrentLocalScope(out var localScope))
        {
            localScope![name] = value;
            return;
        }

        context.SetValue(name, value);
    }

    private void AssignVariable(string name, RuntimeValue value, RuntimeContext context)
    {
        if (TryGetCurrentLocalScope(out var localScope))
        {
            localScope![name] = value;
            return;
        }

        context.SetValue(name, value);
    }

    private bool TryGetVariable(string name, RuntimeContext context, out object? value)
    {
        if (TryGetCurrentLocalScope(out var localScope) && localScope!.TryGetValue(name, out var runtimeValue))
        {
            value = runtimeValue.Value;
            return true;
        }

        return context.TryGet(name, out value);
    }

    private bool TryGetCurrentLocalScope(out Dictionary<string, RuntimeValue>? localScope)
    {
        if (_localScopes.Count > 0)
        {
            localScope = _localScopes.Peek();
            return true;
        }

        localScope = null;
        return false;
    }

    private int CurrentFunctionLoopBoundary => _functionLoopBoundaries.Count > 0 ? _functionLoopBoundaries.Peek() : 0;

    private RuntimeValue Evaluate(Expression expression, RuntimeContext context)
    {
        return expression switch
        {
            LiteralExpression literal => RuntimeValue.FromObject(literal.Value),
            IdentifierExpression identifier => EvaluateIdentifier(identifier, context),
            UnaryExpression unary => EvaluateUnary(unary, context),
            BinaryExpression binary => EvaluateBinary(binary, context),
            FunctionCallExpression functionCall => EvaluateFunctionCall(functionCall, context),
            ArrayExpression array => EvaluateArray(array, context),
            IndexExpression index => EvaluateIndex(index, context),
            MemberAccessExpression memberAccess => EvaluateMemberAccess(memberAccess, context),
            _ => throw new RuntimeException($"Unsupported expression type '{expression.GetType().Name}'.")
        };
    }

    private RuntimeValue EvaluateArray(ArrayExpression expression, RuntimeContext context)
    {
        var values = expression.Elements
            .Select(element => Evaluate(element, context).Value)
            .ToList();

        return new RuntimeValue(values);
    }

    private RuntimeValue EvaluateIndex(IndexExpression expression, RuntimeContext context)
    {
        var target = Evaluate(expression.Target, context).Value;
        var indexValue = Evaluate(expression.Index, context).Value;

        if (!TryGetInteger(indexValue, out var index))
        {
            throw new RuntimeException("Array index must be an int value.", expression.Line, expression.Column, "[");
        }

        if (target is not IList list)
        {
            throw new RuntimeException("Index access requires an array value.", expression.Line, expression.Column, "[");
        }

        if (index < 0 || index >= list.Count)
        {
            throw new RuntimeException($"Array index {index} is outside the array bounds.", expression.Line, expression.Column, "[");
        }

        return RuntimeValue.FromObject(list[index]);
    }

    private RuntimeValue EvaluateMemberAccess(MemberAccessExpression expression, RuntimeContext context)
    {
        var target = Evaluate(expression.Target, context).Value;

        if (target is null)
        {
            throw new RuntimeException($"Cannot access property '{expression.MemberName}' on null.", expression.Line, expression.Column, expression.MemberName);
        }

        if (target is IDictionary<string, object?> dictionary)
        {
            if (dictionary.TryGetValue(expression.MemberName, out var dictionaryValue))
            {
                return RuntimeValue.FromObject(dictionaryValue);
            }
        }

        if (target is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            if (readOnlyDictionary.TryGetValue(expression.MemberName, out var dictionaryValue))
            {
                return RuntimeValue.FromObject(dictionaryValue);
            }
        }

        var property = target.GetType().GetProperty(
            expression.MemberName,
            BindingFlags.Instance | BindingFlags.Public);

        if (property is not null)
        {
            return RuntimeValue.FromObject(property.GetValue(target));
        }

        throw new RuntimeException($"Property '{expression.MemberName}' was not found.", expression.Line, expression.Column, expression.MemberName);
    }

    private RuntimeValue EvaluateUnary(UnaryExpression expression, RuntimeContext context)
    {
        var operand = Evaluate(expression.Operand, context).Value;

        return expression.Operator switch
        {
            TokenType.Minus when TryGetNumber(operand, out var number) => new RuntimeValue(-number),
            TokenType.Minus => throw new RuntimeException("Unary '-' requires a number operand.", expression.Line, expression.Column, expression.TokenText),
            TokenType.Bang when operand is bool boolValue => new RuntimeValue(!boolValue),
            TokenType.Bang => throw new RuntimeException("Unary '!' requires a bool operand.", expression.Line, expression.Column, expression.TokenText),
            _ => throw new RuntimeException($"Unsupported unary operator '{expression.Operator}'.", expression.Line, expression.Column, expression.TokenText)
        };
    }

    private RuntimeValue EvaluateBinary(BinaryExpression expression, RuntimeContext context)
    {
        var left = Evaluate(expression.Left, context).Value;
        var right = Evaluate(expression.Right, context).Value;

        return expression.Operator switch
        {
            TokenType.Plus => Add(left, right, expression),
            TokenType.Minus => NumberOperation(left, right, "-", expression, (a, b) => a - b),
            TokenType.Star => NumberOperation(left, right, "*", expression, (a, b) => a * b),
            TokenType.Slash => NumberOperation(left, right, "/", expression, (a, b) => a / b),
            TokenType.Percent => NumberOperation(left, right, "%", expression, (a, b) => a % b),
            TokenType.Greater => NumberComparison(left, right, ">", expression, (a, b) => a > b),
            TokenType.GreaterOrEqual => NumberComparison(left, right, ">=", expression, (a, b) => a >= b),
            TokenType.Less => NumberComparison(left, right, "<", expression, (a, b) => a < b),
            TokenType.LessOrEqual => NumberComparison(left, right, "<=", expression, (a, b) => a <= b),
            TokenType.EqualEqual => new RuntimeValue(AreEqual(left, right)),
            TokenType.BangEqual => new RuntimeValue(!AreEqual(left, right)),
            _ => throw new RuntimeException($"Unsupported binary operator '{expression.Operator}'.", expression.Line, expression.Column, expression.TokenText)
        };
    }

    private RuntimeValue EvaluateIdentifier(IdentifierExpression expression, RuntimeContext context)
    {
        if (TryGetVariable(expression.Name, context, out var value))
        {
            return RuntimeValue.FromObject(value);
        }

        throw new RuntimeException($"Undefined variable '{expression.Name}'.", expression.Line, expression.Column, expression.Name);
    }

    private RuntimeValue EvaluateFunctionCall(FunctionCallExpression expression, RuntimeContext context)
    {
        var arguments = expression.Arguments
            .Select(argument => Evaluate(argument, context))
            .ToArray();

        if (_userFunctions.TryGetValue(expression.Name, out var userFunction))
        {
            return InvokeUserFunction(userFunction, arguments, context, expression.Line, expression.Column);
        }

        if (_hostFunctions.TryGetValue(expression.Name, out var hostFunction))
        {
            return InvokeHostFunction(expression.Name, hostFunction, arguments, expression.Line, expression.Column);
        }

        try
        {
            if (_builtinFunctions.TryInvoke(expression.Name, arguments, out var builtinValue))
            {
                return builtinValue;
            }
        }
        catch (RuntimeException exception)
        {
            throw new RuntimeException(exception.Message, exception, expression.Line, expression.Column, expression.Name);
        }

        throw new RuntimeException($"Function '{expression.Name}' is not registered.", expression.Line, expression.Column, expression.Name);
    }

    private RuntimeValue InvokeUserFunction(
        FunctionDeclarationStatement function,
        IReadOnlyList<RuntimeValue> arguments,
        RuntimeContext context,
        int? line,
        int? column)
    {
        if (arguments.Count != function.Parameters.Count)
        {
            throw new RuntimeException($"Function '{function.Name}' expects {function.Parameters.Count} argument(s), but received {arguments.Count}.", line, column, function.Name);
        }

        if (_callStack.Contains(function.Name, StringComparer.Ordinal))
        {
            throw new RuntimeException($"Function '{function.Name}' recursion is not supported.", line, column, function.Name);
        }

        var localScope = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            localScope[function.Parameters[i]] = arguments[i];
        }

        _callStack.Push(function.Name);
        _localScopes.Push(localScope);
        _functionLoopBoundaries.Push(_loopDepth);

        try
        {
            foreach (var statement in function.Body)
            {
                ExecuteStatement(statement, context);
            }

            return RuntimeValue.Null;
        }
        catch (ReturnSignalException signal)
        {
            return signal.Value;
        }
        finally
        {
            _functionLoopBoundaries.Pop();
            _localScopes.Pop();
            _callStack.Pop();
        }
    }

    private static RuntimeValue InvokeHostFunction(
        string name,
        Func<IReadOnlyList<object?>, object?> function,
        IReadOnlyList<RuntimeValue> arguments,
        int? line,
        int? column)
    {
        try
        {
            var hostArguments = arguments.Select(argument => argument.Value).ToArray();
            var result = function(hostArguments);

            return RuntimeValue.FromObject(result);
        }
        catch (Exception exception) when (exception is not RuntimeException)
        {
            throw new RuntimeException($"Host function '{name}' failed.", exception, line, column, name);
        }
        catch (RuntimeException exception)
        {
            throw new RuntimeException($"Host function '{name}' failed: {exception.Message}", exception, line, column, name);
        }
    }

    private static RuntimeValue Add(object? left, object? right, BinaryExpression expression)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return new RuntimeValue(leftNumber + rightNumber);
        }

        if (left is string && right is string)
        {
            return new RuntimeValue((string)left + (string)right);
        }

        if (left is string || right is string)
        {
            return new RuntimeValue(ConvertToString(left) + ConvertToString(right));
        }

        throw new RuntimeException("Operator '+' requires number operands or at least one string operand.", expression.Line, expression.Column, expression.TokenText);
    }

    private static RuntimeValue NumberOperation(object? left, object? right, string operatorText, BinaryExpression expression, Func<double, double, double> operation)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return new RuntimeValue(operation(leftNumber, rightNumber));
        }

        throw new RuntimeException($"Operator '{operatorText}' requires number operands.", expression.Line, expression.Column, expression.TokenText);
    }

    private static RuntimeValue NumberComparison(object? left, object? right, string operatorText, BinaryExpression expression, Func<double, double, bool> comparison)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return new RuntimeValue(comparison(leftNumber, rightNumber));
        }

        throw new RuntimeException($"Operator '{operatorText}' requires number operands.", expression.Line, expression.Column, expression.TokenText);
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.Equals(rightNumber);
        }

        return (left, right) switch
        {
            (string leftString, string rightString) => leftString == rightString,
            (bool leftBool, bool rightBool) => leftBool == rightBool,
            (null, null) => true,
            _ => false
        };
    }

    private static bool TryGetNumber(object? value, out double number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case double doubleValue:
                number = doubleValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static IEnumerable<object?> GetIterableItems(object? value, ForeachStatement statement)
    {
        return value switch
        {
            List<object?> list => list,
            object?[] array => array,
            IEnumerable<object?> enumerable => enumerable,
            string text => text.Select(character => character.ToString()),
            _ => throw new RuntimeException("foreach requires an iterable value.", statement.Line, statement.Column, "foreach")
        };
    }

    private static bool TryGetInteger(object? value, out int intValue)
    {
        switch (value)
        {
            case byte byteValue:
                intValue = byteValue;
                return true;
            case short shortValue:
                intValue = shortValue;
                return true;
            case int directValue:
                intValue = directValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)longValue;
                return true;
            case float floatValue when IsWholeNumber(floatValue) && floatValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)floatValue;
                return true;
            case double doubleValue when IsWholeNumber(doubleValue) && doubleValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)doubleValue;
                return true;
            case decimal decimalValue when decimalValue == decimal.Truncate(decimalValue) && decimalValue is >= int.MinValue and <= int.MaxValue:
                intValue = (int)decimalValue;
                return true;
            default:
                intValue = 0;
                return false;
        }
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool IsWholeNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Truncate(value);
    }
}
