using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class Interpreter
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions;

    public Interpreter(BuiltinFunctions builtinFunctions)
        : this(builtinFunctions, new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal))
    {
    }

    public Interpreter(
        BuiltinFunctions builtinFunctions,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> hostFunctions)
    {
        _builtinFunctions = builtinFunctions ?? throw new ArgumentNullException(nameof(builtinFunctions));
        _hostFunctions = hostFunctions ?? throw new ArgumentNullException(nameof(hostFunctions));
    }

    public void Execute(IReadOnlyList<Statement> statements, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var statement in statements)
        {
            ExecuteStatement(statement, context);
        }
    }

    private void ExecuteStatement(Statement statement, RuntimeContext context)
    {
        switch (statement)
        {
            case VarStatement varStatement:
                context.SetValue(varStatement.Name, varStatement.Initializer is null ? RuntimeValue.Null : Evaluate(varStatement.Initializer, context));
                break;
            case AssignmentStatement assignmentStatement:
                context.SetValue(assignmentStatement.Name, Evaluate(assignmentStatement.Value, context));
                break;
            case ExpressionStatement expressionStatement:
                Evaluate(expressionStatement.Expression, context);
                break;
            case IfStatement ifStatement:
                ExecuteIfStatement(ifStatement, context);
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

    private RuntimeValue Evaluate(Expression expression, RuntimeContext context)
    {
        return expression switch
        {
            LiteralExpression literal => RuntimeValue.FromObject(literal.Value),
            IdentifierExpression identifier => EvaluateIdentifier(identifier, context),
            UnaryExpression unary => EvaluateUnary(unary, context),
            BinaryExpression binary => EvaluateBinary(binary, context),
            FunctionCallExpression functionCall => EvaluateFunctionCall(functionCall, context),
            _ => throw new RuntimeException($"Unsupported expression type '{expression.GetType().Name}'.")
        };
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

    private static RuntimeValue EvaluateIdentifier(IdentifierExpression expression, RuntimeContext context)
    {
        if (context.TryGet(expression.Name, out var value))
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

            if (IsSupportedHostReturnValue(result))
            {
                return RuntimeValue.FromObject(result);
            }

            throw new RuntimeException($"Host function '{name}' returned unsupported type '{result!.GetType().Name}'.", line, column, name);
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

    private static bool IsSupportedHostReturnValue(object? value)
    {
        return value is null
            or string
            or bool
            or byte
            or short
            or int
            or long
            or float
            or double
            or decimal;
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
}
