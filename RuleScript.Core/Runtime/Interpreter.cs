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
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>> _asyncHostFunctions;
    private readonly Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>> _notifyRuntimeEventAsync;
    private readonly int _maxLoopIterations;
    private readonly ScriptModule _mainModule;
    private readonly Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective> _notifyRuntimeEvent;
    private readonly Func<RuleScriptSourceLocation, bool> _isBreakpoint;
    private readonly Func<bool> _isStepExecution;
    private readonly Stack<ScriptModule> _moduleStack = new();
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
        : this(
            builtinFunctions,
            hostFunctions,
            new Dictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>>(StringComparer.Ordinal),
            maxLoopIterations,
            new ScriptModule("<script>", []),
            _ => RuleScriptExecutionDirective.Continue,
            (_, _) => Task.FromResult(RuleScriptExecutionDirective.Continue),
            _ => false,
            () => false)
    {
    }

    internal Interpreter(
        BuiltinFunctions builtinFunctions,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> hostFunctions,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>> asyncHostFunctions,
        int maxLoopIterations,
        ScriptModule mainModule,
        Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective> notifyRuntimeEvent,
        Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>> notifyRuntimeEventAsync,
        Func<RuleScriptSourceLocation, bool> isBreakpoint,
        Func<bool> isStepExecution)
    {
        _builtinFunctions = builtinFunctions ?? throw new ArgumentNullException(nameof(builtinFunctions));
        _hostFunctions = hostFunctions ?? throw new ArgumentNullException(nameof(hostFunctions));
        _asyncHostFunctions = asyncHostFunctions ?? throw new ArgumentNullException(nameof(asyncHostFunctions));
        _mainModule = mainModule ?? throw new ArgumentNullException(nameof(mainModule));
        _notifyRuntimeEvent = notifyRuntimeEvent ?? throw new ArgumentNullException(nameof(notifyRuntimeEvent));
        _notifyRuntimeEventAsync = notifyRuntimeEventAsync ?? throw new ArgumentNullException(nameof(notifyRuntimeEventAsync));
        _isBreakpoint = isBreakpoint ?? throw new ArgumentNullException(nameof(isBreakpoint));
        _isStepExecution = isStepExecution ?? throw new ArgumentNullException(nameof(isStepExecution));
        _maxLoopIterations = maxLoopIterations > 0
            ? maxLoopIterations
            : throw new ArgumentOutOfRangeException(nameof(maxLoopIterations), "Max loop iterations must be greater than zero.");
    }

    public void Execute(IReadOnlyList<Statement> statements, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(context);

        var module = new ScriptModule("<script>", statements);

        foreach (var statement in statements)
        {
            if (statement is FunctionDeclarationStatement functionDeclaration)
            {
                module.Functions[functionDeclaration.Name] = new UserFunction(functionDeclaration, module);
            }
        }

        Execute(module, context);
    }

    internal void Execute(ScriptModule module, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);

        _moduleStack.Push(module);

        try
        {
            foreach (var statement in module.Statements)
            {
                if (statement is not FunctionDeclarationStatement and not ImportStatement)
                {
                    ExecuteStatement(statement, context);
                }
            }
        }
        finally
        {
            _moduleStack.Pop();
        }
    }

    public Task ExecuteAsync(IReadOnlyList<Statement> statements, RuntimeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(context);

        var module = new ScriptModule("<script>", statements);

        foreach (var statement in statements)
        {
            if (statement is FunctionDeclarationStatement functionDeclaration)
            {
                module.Functions[functionDeclaration.Name] = new UserFunction(functionDeclaration, module);
            }
        }

        return ExecuteAsync(module, context, cancellationToken);
    }

    internal async Task ExecuteAsync(ScriptModule module, RuntimeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);

        _moduleStack.Push(module);

        try
        {
            foreach (var statement in module.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement is not FunctionDeclarationStatement and not ImportStatement)
                {
                    await ExecuteStatementAsync(statement, context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _moduleStack.Pop();
        }
    }

    private void ExecuteStatement(Statement statement, RuntimeContext context)
    {
        ReportStatementLocation(statement, context);

        try
        {
            switch (statement)
            {
                case VarStatement varStatement:
                    DeclareVariable(varStatement.Name, varStatement.Initializer is null ? RuntimeValue.Null : Evaluate(varStatement.Initializer, context), context);
                    break;
                case AssignmentStatement assignmentStatement:
                    AssignVariable(assignmentStatement.Name, Evaluate(assignmentStatement.Value, context), context);
                    break;
                case GlobalAssignmentStatement globalAssignmentStatement:
                    AssignGlobalVariable(globalAssignmentStatement.Name, Evaluate(globalAssignmentStatement.Value, context), context);
                    break;
                case ExpressionStatement expressionStatement:
                    Evaluate(expressionStatement.Expression, context);
                    break;
                case FunctionDeclarationStatement:
                    break;
                case ImportStatement:
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
        catch (RuntimeException exception)
        {
            exception.SourceFile ??= CurrentModule.Name;
            throw;
        }
    }

    private async Task ExecuteStatementAsync(Statement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReportStatementLocationAsync(statement, context, cancellationToken).ConfigureAwait(false);

        try
        {
            switch (statement)
            {
                case VarStatement varStatement:
                    DeclareVariable(varStatement.Name, varStatement.Initializer is null ? RuntimeValue.Null : await EvaluateAsync(varStatement.Initializer, context, cancellationToken).ConfigureAwait(false), context);
                    break;
                case AssignmentStatement assignmentStatement:
                    AssignVariable(assignmentStatement.Name, await EvaluateAsync(assignmentStatement.Value, context, cancellationToken).ConfigureAwait(false), context);
                    break;
                case GlobalAssignmentStatement globalAssignmentStatement:
                    AssignGlobalVariable(globalAssignmentStatement.Name, await EvaluateAsync(globalAssignmentStatement.Value, context, cancellationToken).ConfigureAwait(false), context);
                    break;
                case ExpressionStatement expressionStatement:
                    await EvaluateAsync(expressionStatement.Expression, context, cancellationToken).ConfigureAwait(false);
                    break;
                case FunctionDeclarationStatement:
                    break;
                case ImportStatement:
                    break;
                case ReturnStatement returnStatement:
                    await ExecuteReturnStatementAsync(returnStatement, context, cancellationToken).ConfigureAwait(false);
                    break;
                case IfStatement ifStatement:
                    await ExecuteIfStatementAsync(ifStatement, context, cancellationToken).ConfigureAwait(false);
                    break;
                case WhileStatement whileStatement:
                    await ExecuteWhileStatementAsync(whileStatement, context, cancellationToken).ConfigureAwait(false);
                    break;
                case ForeachStatement foreachStatement:
                    await ExecuteForeachStatementAsync(foreachStatement, context, cancellationToken).ConfigureAwait(false);
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
        catch (RuntimeException exception)
        {
            exception.SourceFile ??= CurrentModule.Name;
            throw;
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

    private async Task ExecuteIfStatementAsync(IfStatement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        var condition = (await EvaluateAsync(statement.Condition, context, cancellationToken).ConfigureAwait(false)).Value;

        if (condition is not bool conditionValue)
        {
            throw new RuntimeException("If condition must evaluate to a bool value.", statement.Line, statement.Column, "if");
        }

        var branch = conditionValue ? statement.ThenBranch : statement.ElseBranch;

        foreach (var childStatement in branch)
        {
            await ExecuteStatementAsync(childStatement, context, cancellationToken).ConfigureAwait(false);
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

    private async Task ExecuteWhileStatementAsync(WhileStatement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        var iterations = 0;
        _loopDepth++;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (iterations >= _maxLoopIterations)
                {
                    throw new RuntimeException($"while exceeded max loop iterations limit {_maxLoopIterations}.", statement.Line, statement.Column, "while");
                }

                var condition = (await EvaluateAsync(statement.Condition, context, cancellationToken).ConfigureAwait(false)).Value;

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
                        await ExecuteStatementAsync(childStatement, context, cancellationToken).ConfigureAwait(false);
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

    private async Task ExecuteForeachStatementAsync(ForeachStatement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        var iterableValue = (await EvaluateAsync(statement.Iterable, context, cancellationToken).ConfigureAwait(false)).Value;
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
                cancellationToken.ThrowIfCancellationRequested();

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
                        await ExecuteStatementAsync(childStatement, context, cancellationToken).ConfigureAwait(false);
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

    private async Task ExecuteReturnStatementAsync(ReturnStatement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        if (_localScopes.Count == 0)
        {
            throw new RuntimeException("return can only be used inside a function.", statement.Line, statement.Column, "return");
        }

        var value = statement.Value is null ? RuntimeValue.Null : await EvaluateAsync(statement.Value, context, cancellationToken).ConfigureAwait(false);
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

    private static void AssignGlobalVariable(string name, RuntimeValue value, RuntimeContext context)
    {
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

    private ScriptModule CurrentModule => _moduleStack.Count > 0 ? _moduleStack.Peek() : _mainModule;

    private RuntimeValue Evaluate(Expression expression, RuntimeContext context)
    {
        return expression switch
        {
            LiteralExpression literal => RuntimeValue.FromObject(literal.Value),
            IdentifierExpression identifier => EvaluateIdentifier(identifier, context),
            GlobalIdentifierExpression globalIdentifier => EvaluateGlobalIdentifier(globalIdentifier, context),
            UnaryExpression unary => EvaluateUnary(unary, context),
            BinaryExpression binary => EvaluateBinary(binary, context),
            FunctionCallExpression functionCall => EvaluateFunctionCall(functionCall, context),
            ModuleFunctionCallExpression moduleFunctionCall => EvaluateModuleFunctionCall(moduleFunctionCall, context),
            ArrayExpression array => EvaluateArray(array, context),
            IndexExpression index => EvaluateIndex(index, context),
            MemberAccessExpression memberAccess => EvaluateMemberAccess(memberAccess, context),
            _ => throw new RuntimeException($"Unsupported expression type '{expression.GetType().Name}'.")
        };
    }

    private Task<RuntimeValue> EvaluateAsync(Expression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return expression switch
        {
            LiteralExpression literal => Task.FromResult(RuntimeValue.FromObject(literal.Value)),
            IdentifierExpression identifier => Task.FromResult(EvaluateIdentifier(identifier, context)),
            GlobalIdentifierExpression globalIdentifier => Task.FromResult(EvaluateGlobalIdentifier(globalIdentifier, context)),
            UnaryExpression unary => EvaluateUnaryAsync(unary, context, cancellationToken),
            BinaryExpression binary => EvaluateBinaryAsync(binary, context, cancellationToken),
            FunctionCallExpression functionCall => EvaluateFunctionCallAsync(functionCall, context, cancellationToken),
            ModuleFunctionCallExpression moduleFunctionCall => EvaluateModuleFunctionCallAsync(moduleFunctionCall, context, cancellationToken),
            ArrayExpression array => EvaluateArrayAsync(array, context, cancellationToken),
            IndexExpression index => EvaluateIndexAsync(index, context, cancellationToken),
            MemberAccessExpression memberAccess => EvaluateMemberAccessAsync(memberAccess, context, cancellationToken),
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

    private async Task<RuntimeValue> EvaluateArrayAsync(ArrayExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var values = new List<object?>(expression.Elements.Count);

        foreach (var element in expression.Elements)
        {
            values.Add((await EvaluateAsync(element, context, cancellationToken).ConfigureAwait(false)).Value);
        }

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

    private async Task<RuntimeValue> EvaluateIndexAsync(IndexExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var target = (await EvaluateAsync(expression.Target, context, cancellationToken).ConfigureAwait(false)).Value;
        var indexValue = (await EvaluateAsync(expression.Index, context, cancellationToken).ConfigureAwait(false)).Value;

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

    private async Task<RuntimeValue> EvaluateMemberAccessAsync(MemberAccessExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var target = (await EvaluateAsync(expression.Target, context, cancellationToken).ConfigureAwait(false)).Value;

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

    private async Task<RuntimeValue> EvaluateUnaryAsync(UnaryExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var operand = (await EvaluateAsync(expression.Operand, context, cancellationToken).ConfigureAwait(false)).Value;

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
            TokenType.And => BooleanOperation(left, right, "and", expression, (a, b) => a && b),
            TokenType.Or => BooleanOperation(left, right, "or", expression, (a, b) => a || b),
            _ => throw new RuntimeException($"Unsupported binary operator '{expression.Operator}'.", expression.Line, expression.Column, expression.TokenText)
        };
    }

    private async Task<RuntimeValue> EvaluateBinaryAsync(BinaryExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var left = (await EvaluateAsync(expression.Left, context, cancellationToken).ConfigureAwait(false)).Value;
        var right = (await EvaluateAsync(expression.Right, context, cancellationToken).ConfigureAwait(false)).Value;

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
            TokenType.And => BooleanOperation(left, right, "and", expression, (a, b) => a && b),
            TokenType.Or => BooleanOperation(left, right, "or", expression, (a, b) => a || b),
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

    private static RuntimeValue EvaluateGlobalIdentifier(GlobalIdentifierExpression expression, RuntimeContext context)
    {
        if (context.TryGet(expression.Name, out var value))
        {
            return RuntimeValue.FromObject(value);
        }

        throw new RuntimeException($"Undefined global variable '{expression.Name}'.", expression.Line, expression.Column, expression.Name);
    }

    private RuntimeValue EvaluateFunctionCall(FunctionCallExpression expression, RuntimeContext context)
    {
        var arguments = expression.Arguments
            .Select(argument => Evaluate(argument, context))
            .ToArray();

        if (CurrentModule.Functions.TryGetValue(expression.Name, out var userFunction))
        {
            return InvokeUserFunction(userFunction, arguments, context, expression.Line, expression.Column);
        }

        if (_hostFunctions.TryGetValue(expression.Name, out var hostFunction))
        {
            return InvokeHostFunction(expression.Name, hostFunction, arguments, expression.Line, expression.Column);
        }

        if (_asyncHostFunctions.ContainsKey(expression.Name))
        {
            throw new RuntimeException($"Async host function '{expression.Name}' requires ExecuteAsync.", expression.Line, expression.Column, expression.Name);
        }

        try
        {
            if (_builtinFunctions.TryInvoke(expression.Name, arguments, out var builtinValue))
            {
                if (expression.Name == "Print")
                {
                    NotifyPrint(expression, builtinValue);
                }

                return builtinValue;
            }
        }
        catch (RuntimeException exception)
        {
            throw new RuntimeException(exception.Message, exception, expression.Line, expression.Column, expression.Name);
        }

        throw new RuntimeException($"Function '{expression.Name}' is not registered.", expression.Line, expression.Column, expression.Name);
    }

    private async Task<RuntimeValue> EvaluateFunctionCallAsync(FunctionCallExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var arguments = new RuntimeValue[expression.Arguments.Count];

        for (var i = 0; i < expression.Arguments.Count; i++)
        {
            arguments[i] = await EvaluateAsync(expression.Arguments[i], context, cancellationToken).ConfigureAwait(false);
        }

        if (CurrentModule.Functions.TryGetValue(expression.Name, out var userFunction))
        {
            return await InvokeUserFunctionAsync(userFunction, arguments, context, expression.Line, expression.Column, cancellationToken).ConfigureAwait(false);
        }

        if (_asyncHostFunctions.TryGetValue(expression.Name, out var asyncHostFunction))
        {
            return await InvokeHostFunctionAsync(expression.Name, asyncHostFunction, arguments, expression.Line, expression.Column, cancellationToken).ConfigureAwait(false);
        }

        if (_hostFunctions.TryGetValue(expression.Name, out var hostFunction))
        {
            return InvokeHostFunction(expression.Name, hostFunction, arguments, expression.Line, expression.Column);
        }

        try
        {
            if (_builtinFunctions.TryInvoke(expression.Name, arguments, out var builtinValue))
            {
                if (expression.Name == "Print")
                {
                    await NotifyPrintAsync(expression, builtinValue, cancellationToken).ConfigureAwait(false);
                }

                return builtinValue;
            }
        }
        catch (RuntimeException exception)
        {
            throw new RuntimeException(exception.Message, exception, expression.Line, expression.Column, expression.Name);
        }

        throw new RuntimeException($"Function '{expression.Name}' is not registered.", expression.Line, expression.Column, expression.Name);
    }

    private RuntimeValue EvaluateModuleFunctionCall(ModuleFunctionCallExpression expression, RuntimeContext context)
    {
        var arguments = expression.Arguments
            .Select(argument => Evaluate(argument, context))
            .ToArray();

        var module = ResolveAlias(expression.ModuleName, expression.Line, expression.Column);

        if (!module.Functions.TryGetValue(expression.FunctionName, out var userFunction))
        {
            throw new RuntimeException(
                $"Module alias '{expression.ModuleName}' function not found: '{expression.FunctionName}'.",
                expression.Line,
                expression.Column,
                expression.FunctionName);
        }

        return InvokeUserFunction(userFunction, arguments, context, expression.Line, expression.Column);
    }

    private async Task<RuntimeValue> EvaluateModuleFunctionCallAsync(ModuleFunctionCallExpression expression, RuntimeContext context, CancellationToken cancellationToken)
    {
        var arguments = new RuntimeValue[expression.Arguments.Count];

        for (var i = 0; i < expression.Arguments.Count; i++)
        {
            arguments[i] = await EvaluateAsync(expression.Arguments[i], context, cancellationToken).ConfigureAwait(false);
        }

        var module = ResolveAlias(expression.ModuleName, expression.Line, expression.Column);

        if (!module.Functions.TryGetValue(expression.FunctionName, out var userFunction))
        {
            throw new RuntimeException(
                $"Module alias '{expression.ModuleName}' function not found: '{expression.FunctionName}'.",
                expression.Line,
                expression.Column,
                expression.FunctionName);
        }

        return await InvokeUserFunctionAsync(userFunction, arguments, context, expression.Line, expression.Column, cancellationToken).ConfigureAwait(false);
    }

    private RuntimeValue InvokeUserFunction(
        UserFunction userFunction,
        IReadOnlyList<RuntimeValue> arguments,
        RuntimeContext context,
        int? line,
        int? column)
    {
        var function = userFunction.Declaration;

        if (arguments.Count != function.Parameters.Count)
        {
            throw new RuntimeException($"Function '{function.Name}' expects {function.Parameters.Count} argument(s), but received {arguments.Count}.", line, column, function.Name);
        }

        var callId = $"{userFunction.Module.Name}::{function.Name}";

        if (_callStack.Contains(callId, StringComparer.Ordinal))
        {
            throw new RuntimeException($"Function '{function.Name}' recursion is not supported.", line, column, function.Name);
        }

        var localScope = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            localScope[function.Parameters[i]] = arguments[i];
        }

        _callStack.Push(callId);
        _moduleStack.Push(userFunction.Module);
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
            _moduleStack.Pop();
            _callStack.Pop();
        }
    }

    private async Task<RuntimeValue> InvokeUserFunctionAsync(
        UserFunction userFunction,
        IReadOnlyList<RuntimeValue> arguments,
        RuntimeContext context,
        int? line,
        int? column,
        CancellationToken cancellationToken)
    {
        var function = userFunction.Declaration;

        if (arguments.Count != function.Parameters.Count)
        {
            throw new RuntimeException($"Function '{function.Name}' expects {function.Parameters.Count} argument(s), but received {arguments.Count}.", line, column, function.Name);
        }

        var callId = $"{userFunction.Module.Name}::{function.Name}";

        if (_callStack.Contains(callId, StringComparer.Ordinal))
        {
            throw new RuntimeException($"Function '{function.Name}' recursion is not supported.", line, column, function.Name);
        }

        var localScope = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            localScope[function.Parameters[i]] = arguments[i];
        }

        _callStack.Push(callId);
        _moduleStack.Push(userFunction.Module);
        _localScopes.Push(localScope);
        _functionLoopBoundaries.Push(_loopDepth);

        try
        {
            foreach (var statement in function.Body)
            {
                await ExecuteStatementAsync(statement, context, cancellationToken).ConfigureAwait(false);
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
            _moduleStack.Pop();
            _callStack.Pop();
        }
    }

    private ScriptModule ResolveAlias(string alias, int? line, int? column)
    {
        if (CurrentModule.Aliases.TryGetValue(alias, out var module))
        {
            return module;
        }

        if (!ReferenceEquals(CurrentModule, _mainModule) && _mainModule.Aliases.TryGetValue(alias, out module))
        {
            return module;
        }

        throw new RuntimeException($"Unknown alias '{alias}'.", line, column, alias);
    }

    private void ReportStatementLocation(Statement statement, RuntimeContext context)
    {
        var location = GetStatementLocation(statement);
        context.CurrentLocation = location;

        _notifyRuntimeEvent(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.CurrentLineChanged,
            location));

        var breakpointHit = _isBreakpoint(location);

        if (breakpointHit)
        {
            _notifyRuntimeEvent(new RuleScriptRuntimeEvent(
                RuleScriptRuntimeEventKind.BreakpointHit,
                location,
                $"Breakpoint hit at {location.File}:{location.Line}."));
        }

        if (!breakpointHit && _isStepExecution())
        {
            _notifyRuntimeEvent(new RuleScriptRuntimeEvent(
                RuleScriptRuntimeEventKind.StepPaused,
                location,
                $"Step paused at {location.File}:{location.Line}."));
        }
    }

    private async Task ReportStatementLocationAsync(Statement statement, RuntimeContext context, CancellationToken cancellationToken)
    {
        var location = GetStatementLocation(statement);
        context.CurrentLocation = location;

        await _notifyRuntimeEventAsync(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.CurrentLineChanged,
            location), cancellationToken).ConfigureAwait(false);

        var breakpointHit = _isBreakpoint(location);

        if (breakpointHit)
        {
            await _notifyRuntimeEventAsync(new RuleScriptRuntimeEvent(
                RuleScriptRuntimeEventKind.BreakpointHit,
                location,
                $"Breakpoint hit at {location.File}:{location.Line}."), cancellationToken).ConfigureAwait(false);
        }

        if (!breakpointHit && _isStepExecution())
        {
            await _notifyRuntimeEventAsync(new RuleScriptRuntimeEvent(
                RuleScriptRuntimeEventKind.StepPaused,
                location,
                $"Step paused at {location.File}:{location.Line}."), cancellationToken).ConfigureAwait(false);
        }
    }

    private void NotifyPrint(FunctionCallExpression expression, RuntimeValue value)
    {
        var location = new RuleScriptSourceLocation(CurrentModule.Name, expression.Line, expression.Column);
        _notifyRuntimeEvent(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.Print,
            location,
            ConvertToString(value.Value),
            value.Value));
    }

    private Task NotifyPrintAsync(FunctionCallExpression expression, RuntimeValue value, CancellationToken cancellationToken)
    {
        var location = new RuleScriptSourceLocation(CurrentModule.Name, expression.Line, expression.Column);
        return _notifyRuntimeEventAsync(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.Print,
            location,
            ConvertToString(value.Value),
            value.Value), cancellationToken);
    }

    private RuleScriptSourceLocation GetStatementLocation(Statement statement)
    {
        var (line, column) = statement switch
        {
            VarStatement value => (value.Line, value.Column),
            AssignmentStatement value => (value.Line, value.Column),
            GlobalAssignmentStatement value => (value.Line, value.Column),
            FunctionDeclarationStatement value => (value.Line, value.Column),
            ImportStatement value => (value.Line, value.Column),
            ReturnStatement value => (value.Line, value.Column),
            IfStatement value => (value.Line, value.Column),
            WhileStatement value => (value.Line, value.Column),
            ForeachStatement value => (value.Line, value.Column),
            BreakStatement value => (value.Line, value.Column),
            ContinueStatement value => (value.Line, value.Column),
            ExpressionStatement { Expression: FunctionCallExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: ModuleFunctionCallExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: IdentifierExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: GlobalIdentifierExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: UnaryExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: BinaryExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: IndexExpression value } => (value.Line, value.Column),
            ExpressionStatement { Expression: MemberAccessExpression value } => (value.Line, value.Column),
            _ => (null, null)
        };

        return new RuleScriptSourceLocation(CurrentModule.Name, line, column);
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

    private static async Task<RuntimeValue> InvokeHostFunctionAsync(
        string name,
        Func<IReadOnlyList<object?>, CancellationToken, Task<object?>> function,
        IReadOnlyList<RuntimeValue> arguments,
        int? line,
        int? column,
        CancellationToken cancellationToken)
    {
        try
        {
            var hostArguments = arguments.Select(argument => argument.Value).ToArray();
            var result = await function(hostArguments, cancellationToken).ConfigureAwait(false);

            return RuntimeValue.FromObject(result);
        }
        catch (Exception exception) when (exception is not RuntimeException and not OperationCanceledException)
        {
            throw new RuntimeException($"Async host function '{name}' failed.", exception, line, column, name);
        }
        catch (RuntimeException exception)
        {
            throw new RuntimeException($"Async host function '{name}' failed: {exception.Message}", exception, line, column, name);
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

    private static RuntimeValue BooleanOperation(object? left, object? right, string operatorText, BinaryExpression expression, Func<bool, bool, bool> operation)
    {
        if (left is bool leftBool && right is bool rightBool)
        {
            return new RuntimeValue(operation(leftBool, rightBool));
        }

        throw new RuntimeException($"Operator '{operatorText}' requires bool operands.", expression.Line, expression.Column, expression.TokenText);
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
