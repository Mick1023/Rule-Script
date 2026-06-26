using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class RuleScriptEngine
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>> _asyncHostFunctions = new(StringComparer.Ordinal);
    private readonly List<RuleScriptBreakpoint> _breakpoints = [];
    private readonly object _executionSync = new();
    private IImportResolver _importResolver = new FileSystemImportResolver();
    private CancellationTokenSource? _stopCancellation;

    /// <summary>
    /// Gets or sets the maximum number of iterations allowed for each loop.
    /// </summary>
    public int MaxLoopIterations { get; set; } = 100000;

    /// <summary>
    /// Gets or sets the base directory used for relative <see cref="ExecuteFile(string)"/> paths.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the import resolver used by <see cref="ExecuteFile(string)"/> and import statements.
    /// </summary>
    public IImportResolver ImportResolver
    {
        get => _importResolver;
        set => _importResolver = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets whether execution should pause at each executable statement.
    /// </summary>
    public bool StepExecution { get; set; }

    /// <summary>
    /// Gets the currently registered breakpoints.
    /// </summary>
    public IReadOnlyList<RuleScriptBreakpoint> Breakpoints => _breakpoints.ToArray();

    /// <summary>
    /// Gets a read-only snapshot of registered host function names.
    /// </summary>
    public IReadOnlyList<string> RegisteredFunctionNames =>
        _hostFunctions.Keys
            .Concat(_asyncHostFunctions.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Notifies the host about runtime events. The returned directive controls step execution.
    /// </summary>
    public Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? RuntimeEventHandler { get; set; }

    /// <summary>
    /// Asynchronously notifies the host about runtime events during async execution. The returned directive controls step execution.
    /// </summary>
    public Func<RuleScriptRuntimeEvent, CancellationToken, Task<RuleScriptExecutionDirective>>? RuntimeEventHandlerAsync { get; set; }

    /// <summary>
    /// Creates a RuleScript engine with the default built-in functions.
    /// </summary>
    public RuleScriptEngine()
        : this(new BuiltinFunctions())
    {
    }

    /// <summary>
    /// Creates a RuleScript engine with a custom built-in function registry.
    /// </summary>
    public RuleScriptEngine(BuiltinFunctions builtinFunctions)
    {
        _builtinFunctions = builtinFunctions ?? throw new ArgumentNullException(nameof(builtinFunctions));
    }

    /// <summary>
    /// Registers or replaces a host function.
    /// </summary>
    public void RegisterFunction(string name, Func<IReadOnlyList<object?>, object?> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _hostFunctions[name] = function ?? throw new ArgumentNullException(nameof(function));
    }

    /// <summary>
    /// Registers or replaces an async host function.
    /// </summary>
    public void RegisterFunctionAsync(string name, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _asyncHostFunctions[name] = function ?? throw new ArgumentNullException(nameof(function));
    }

    /// <summary>
    /// Registers or replaces an async host function.
    /// </summary>
    public void RegisterFunctionAsync(string name, Func<IReadOnlyList<object?>, Task<object?>> function)
    {
        if (function is null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        RegisterFunctionAsync(name, (args, _) => function(args));
    }

    /// <summary>
    /// Removes a registered host function.
    /// </summary>
    public bool UnregisterFunction(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        var removed = _hostFunctions.Remove(name);
        return _asyncHostFunctions.Remove(name) || removed;
    }

    /// <summary>
    /// Removes all registered host functions.
    /// </summary>
    public void ClearFunctions()
    {
        _hostFunctions.Clear();
        _asyncHostFunctions.Clear();
    }

    /// <summary>
    /// Gets a read-only snapshot of variable names from a runtime context.
    /// </summary>
    public IReadOnlyList<string> GetVariableNames(RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.VariableNames;
    }

    /// <summary>
    /// Strictly analyzes parseable script text without executing it and returns symbols for editor tooling.
    /// Syntax errors are reported by throwing <see cref="SyntaxException"/>.
    /// </summary>
    public RuleScriptAnalysisResult Analyze(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
        var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var userFunctions = new HashSet<string>(StringComparer.Ordinal);
        var importAliases = new HashSet<string>(StringComparer.Ordinal);

        CollectSymbols(statements, variables, userFunctions, importAliases);

        return new RuleScriptAnalysisResult(
            variables,
            userFunctions,
            RegisteredFunctionNames,
            _builtinFunctions.Names,
            importAliases);
    }

    /// <summary>
    /// Best-effort analyzes script text without executing it, returning diagnostics and partial symbols instead of throwing for syntax errors.
    /// </summary>
    public RuleScriptAnalysisAttempt TryAnalyze(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        try
        {
            return new RuleScriptAnalysisAttempt(Analyze(script), [], success: true);
        }
        catch (SyntaxException exception)
        {
            var diagnostics = new[]
            {
                new RuleScriptDiagnostic(exception.Message, exception.Line, exception.Column, exception.TokenText)
            };

            return new RuleScriptAnalysisAttempt(AnalyzeBestEffort(script), diagnostics, success: false);
        }
    }

    /// <summary>
    /// Adds a breakpoint for any source file at the specified line.
    /// </summary>
    public void AddBreakpoint(int line)
    {
        AddBreakpoint(null, line);
    }

    /// <summary>
    /// Adds a breakpoint for the specified source file and line.
    /// </summary>
    public void AddBreakpoint(string? file, int line)
    {
        if (line <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Breakpoint line must be greater than zero.");
        }

        var breakpoint = new RuleScriptBreakpoint(NormalizeBreakpointFile(file), line);

        if (!_breakpoints.Contains(breakpoint))
        {
            _breakpoints.Add(breakpoint);
        }
    }

    /// <summary>
    /// Removes a breakpoint for any source file at the specified line.
    /// </summary>
    public bool RemoveBreakpoint(int line)
    {
        return RemoveBreakpoint(null, line);
    }

    /// <summary>
    /// Removes a breakpoint for the specified source file and line.
    /// </summary>
    public bool RemoveBreakpoint(string? file, int line)
    {
        var breakpoint = new RuleScriptBreakpoint(NormalizeBreakpointFile(file), line);
        return _breakpoints.Remove(breakpoint);
    }

    /// <summary>
    /// Removes all registered breakpoints.
    /// </summary>
    public void ClearBreakpoints()
    {
        _breakpoints.Clear();
    }

    /// <summary>
    /// Requests cancellation for the currently running script execution.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? stopCancellation;

        lock (_executionSync)
        {
            stopCancellation = _stopCancellation;
        }

        try
        {
            stopCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Executes a script using a new runtime context.
    /// </summary>
    public RuntimeContext Execute(string script)
    {
        var context = new RuntimeContext();
        Execute(script, context);
        return context;
    }

    /// <summary>
    /// Executes a script using the provided runtime context.
    /// </summary>
    public void Execute(string script, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        using var stopCancellation = CreateStopCancellation();

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            var module = BuildModule("<script>", statements, ResolveWorkingDirectory(), [], new(StringComparer.OrdinalIgnoreCase), isImported: false);
            CreateInterpreter(module, stopCancellation.Token).Execute(module, context);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, "<script>");
            NotifyError(exception);
            throw;
        }
        finally
        {
            ClearStopCancellation(stopCancellation);
        }
    }

    /// <summary>
    /// Executes a script asynchronously using a new runtime context.
    /// </summary>
    public async Task<RuntimeContext> ExecuteAsync(string script, CancellationToken cancellationToken = default)
    {
        var context = new RuntimeContext();
        await ExecuteAsync(script, context, cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <summary>
    /// Executes a script asynchronously using the provided runtime context.
    /// </summary>
    public async Task ExecuteAsync(string script, RuntimeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        using var stopCancellation = CreateStopCancellation();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellation.Token);

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            var module = BuildModule("<script>", statements, ResolveWorkingDirectory(), [], new(StringComparer.OrdinalIgnoreCase), isImported: false);
            await CreateInterpreter(module, linkedCancellation.Token)
                .ExecuteAsync(module, context, linkedCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, "<script>");
            await NotifyErrorAsync(exception, linkedCancellation.Token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            ClearStopCancellation(stopCancellation);
        }
    }

    /// <summary>
    /// Executes a script file using a new runtime context.
    /// </summary>
    public RuntimeContext ExecuteFile(string path)
    {
        var context = new RuntimeContext();
        ExecuteFile(path, context);
        return context;
    }

    /// <summary>
    /// Executes a script file using the provided runtime context.
    /// </summary>
    public void ExecuteFile(string path, RuntimeContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Script path cannot be empty.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(context);

        var fullPath = ResolveExecuteFilePath(path);
        using var stopCancellation = CreateStopCancellation();

        try
        {
            var module = LoadModule(fullPath, [], new(StringComparer.OrdinalIgnoreCase), originalPath: path, importingFile: null, isImported: false);
            CreateInterpreter(module, stopCancellation.Token).Execute(module, context);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, fullPath);
            NotifyError(exception);
            throw;
        }
        finally
        {
            ClearStopCancellation(stopCancellation);
        }
    }

    /// <summary>
    /// Executes a script file asynchronously using a new runtime context.
    /// </summary>
    public async Task<RuntimeContext> ExecuteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var context = new RuntimeContext();
        await ExecuteFileAsync(path, context, cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <summary>
    /// Executes a script file asynchronously using the provided runtime context.
    /// </summary>
    public async Task ExecuteFileAsync(string path, RuntimeContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Script path cannot be empty.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(context);

        var fullPath = ResolveExecuteFilePath(path);
        using var stopCancellation = CreateStopCancellation();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellation.Token);

        try
        {
            var module = LoadModule(fullPath, [], new(StringComparer.OrdinalIgnoreCase), originalPath: path, importingFile: null, isImported: false);
            await CreateInterpreter(module, linkedCancellation.Token)
                .ExecuteAsync(module, context, linkedCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, fullPath);
            await NotifyErrorAsync(exception, linkedCancellation.Token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            ClearStopCancellation(stopCancellation);
        }
    }

    private Interpreter CreateInterpreter(ScriptModule module, CancellationToken cancellationToken)
    {
        return new Interpreter(
            _builtinFunctions,
            _hostFunctions,
            _asyncHostFunctions,
            MaxLoopIterations,
            module,
            NotifyRuntimeEvent,
            NotifyRuntimeEventAsync,
            IsBreakpoint,
            () => StepExecution,
            cancellationToken);
    }

    private CancellationTokenSource CreateStopCancellation()
    {
        var stopCancellation = new CancellationTokenSource();

        lock (_executionSync)
        {
            _stopCancellation = stopCancellation;
        }

        return stopCancellation;
    }

    private void ClearStopCancellation(CancellationTokenSource stopCancellation)
    {
        lock (_executionSync)
        {
            if (ReferenceEquals(_stopCancellation, stopCancellation))
            {
                _stopCancellation = null;
            }
        }
    }

    private ScriptModule LoadModule(
        string path,
        Stack<string> importStack,
        Dictionary<string, ScriptModule> moduleCache,
        string originalPath,
        string? importingFile,
        bool isImported)
    {
        path = ImportResolver.GetFullPath(path);

        if (moduleCache.TryGetValue(path, out var cachedModule))
        {
            return cachedModule;
        }

        if (importStack.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            var chain = importStack.Reverse().Append(path);
            throw new RuntimeException($"Circular import detected: {string.Join(" -> ", chain)}.");
        }

        if (!ImportResolver.Exists(path))
        {
            throw importingFile is null
                ? new RuntimeException($"ExecuteFile could not find script '{originalPath}'. Resolved full path: '{path}'.")
                : new RuntimeException($"Import file '{originalPath}' was not found while importing from '{importingFile}'. Resolved full path: '{path}'.");
        }

        importStack.Push(path);

        try
        {
            var script = ImportResolver.ReadAllText(path);
            IReadOnlyList<Statement> statements;

            try
            {
                var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
                statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            }
            catch (RuleScriptException exception)
            {
                AssignSourceFile(exception, path);
                throw;
            }

            var module = BuildModule(
                path,
                statements,
                Path.GetDirectoryName(path) ?? ResolveWorkingDirectory(),
                importStack,
                moduleCache,
                isImported);

            moduleCache[path] = module;
            return module;
        }
        finally
        {
            importStack.Pop();
        }
    }

    private ScriptModule BuildModule(
        string name,
        IReadOnlyList<Statement> statements,
        string baseDirectory,
        Stack<string> importStack,
        Dictionary<string, ScriptModule> moduleCache,
        bool isImported)
    {
        var module = new ScriptModule(name, statements);

        if (isImported)
        {
            var executableStatement = statements.FirstOrDefault(statement => statement is not ImportStatement and not FunctionDeclarationStatement);

            if (executableStatement is not null)
            {
                throw new RuntimeException($"Imported file '{name}' is invalid: top-level executable statements are not allowed in imported files.");
            }
        }

        foreach (var import in statements.OfType<ImportStatement>())
        {
            var importPath = ResolveImportPath(import.Path, baseDirectory);
            var importedModule = LoadModule(
                importPath,
                importStack,
                moduleCache,
                originalPath: import.Path,
                importingFile: name,
                isImported: true);

            if (import.Alias is not null)
            {
                if (module.Aliases.ContainsKey(import.Alias))
                {
                    throw new RuntimeException($"Duplicate import alias '{import.Alias}' in file '{name}'.", import.Line, import.Column, import.Alias);
                }

                module.Aliases[import.Alias] = importedModule;
                continue;
            }

            foreach (var function in importedModule.Functions)
            {
                module.Functions[function.Key] = function.Value;
            }
        }

        foreach (var function in statements.OfType<FunctionDeclarationStatement>())
        {
            module.Functions[function.Name] = new UserFunction(function, module);
        }

        return module;
    }

    private static void CollectSymbols(
        IEnumerable<Statement> statements,
        ISet<string> variables,
        ISet<string> userFunctions,
        ISet<string> importAliases)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VarStatement varStatement:
                    variables.Add(varStatement.Name);
                    break;
                case AssignmentStatement assignmentStatement:
                    variables.Add(assignmentStatement.Name);
                    break;
                case GlobalAssignmentStatement globalAssignmentStatement:
                    variables.Add(globalAssignmentStatement.Name);
                    break;
                case ForeachStatement foreachStatement:
                    variables.Add(foreachStatement.VariableName);
                    CollectSymbols(foreachStatement.Body, variables, userFunctions, importAliases);
                    break;
                case IfStatement ifStatement:
                    CollectSymbols(ifStatement.ThenBranch, variables, userFunctions, importAliases);
                    CollectSymbols(ifStatement.ElseBranch, variables, userFunctions, importAliases);
                    break;
                case WhileStatement whileStatement:
                    CollectSymbols(whileStatement.Body, variables, userFunctions, importAliases);
                    break;
                case FunctionDeclarationStatement functionDeclaration:
                    userFunctions.Add(functionDeclaration.Name);
                    foreach (var parameter in functionDeclaration.Parameters)
                    {
                        variables.Add(parameter);
                    }

                    CollectSymbols(functionDeclaration.Body, variables, userFunctions, importAliases);
                    break;
                case ImportStatement { Alias: not null } importStatement:
                    importAliases.Add(importStatement.Alias);
                    break;
            }
        }
    }

    private RuleScriptAnalysisResult AnalyzeBestEffort(string script)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var userFunctions = new HashSet<string>(StringComparer.Ordinal);
        var importAliases = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            CollectSymbolsFromTokens(tokens, variables, userFunctions, importAliases);
        }
        catch (SyntaxException)
        {
            CollectSymbolsFromText(script, variables, userFunctions, importAliases);
        }

        return new RuleScriptAnalysisResult(
            variables,
            userFunctions,
            RegisteredFunctionNames,
            _builtinFunctions.Names,
            importAliases);
    }

    private static void CollectSymbolsFromTokens(
        IReadOnlyList<Token> tokens,
        ISet<string> variables,
        ISet<string> userFunctions,
        ISet<string> importAliases)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            switch (token.Type)
            {
                case TokenType.Var when TryGetIdentifier(tokens, i + 1, out var variableName):
                    variables.Add(variableName);
                    break;
                case TokenType.Foreach when TryGetIdentifier(tokens, i + 1, out var iteratorName):
                    variables.Add(iteratorName);
                    break;
                case TokenType.Function when TryGetIdentifier(tokens, i + 1, out var functionName):
                    userFunctions.Add(functionName);
                    CollectFunctionParameters(tokens, i + 2, variables);
                    break;
                case TokenType.Import:
                    CollectImportAlias(tokens, i + 1, importAliases);
                    break;
                case TokenType.Identifier when TryGetToken(tokens, i + 1, out var next) && next.Type == TokenType.Assign:
                    variables.Add(token.Lexeme);
                    break;
            }
        }
    }

    private static void CollectFunctionParameters(IReadOnlyList<Token> tokens, int start, ISet<string> variables)
    {
        if (!TryGetToken(tokens, start, out var openParen) || openParen.Type != TokenType.LeftParen)
        {
            return;
        }

        for (var i = start + 1; i < tokens.Count && tokens[i].Type is not TokenType.RightParen and not TokenType.EndOfFile; i++)
        {
            if (tokens[i].Type == TokenType.Identifier)
            {
                variables.Add(tokens[i].Lexeme);
            }
        }
    }

    private static void CollectImportAlias(IReadOnlyList<Token> tokens, int start, ISet<string> importAliases)
    {
        for (var i = start; i < tokens.Count && tokens[i].Type is not TokenType.Semicolon and not TokenType.EndOfFile; i++)
        {
            if (tokens[i].Type == TokenType.As && TryGetIdentifier(tokens, i + 1, out var alias))
            {
                importAliases.Add(alias);
                return;
            }
        }
    }

    private static bool TryGetIdentifier(IReadOnlyList<Token> tokens, int index, out string name)
    {
        if (TryGetToken(tokens, index, out var token) && token.Type == TokenType.Identifier)
        {
            name = token.Lexeme;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetToken(IReadOnlyList<Token> tokens, int index, out Token token)
    {
        if (index >= 0 && index < tokens.Count)
        {
            token = tokens[index];
            return true;
        }

        token = new Token(TokenType.EndOfFile, string.Empty, null, 0, 0);
        return false;
    }

    private static void CollectSymbolsFromText(
        string script,
        ISet<string> variables,
        ISet<string> userFunctions,
        ISet<string> importAliases)
    {
        foreach (var rawLine in script.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.TrimStart();

            if (TryReadIdentifierAfterKeyword(line, "var", out var variableName)
                || TryReadIdentifierAfterKeyword(line, "foreach", out variableName))
            {
                variables.Add(variableName);
            }

            if (TryReadIdentifierAfterKeyword(line, "function", out var functionName))
            {
                userFunctions.Add(functionName);
                CollectParametersFromText(line, variables);
            }

            if (TryReadAssignmentTarget(line, out var assignmentName))
            {
                variables.Add(assignmentName);
            }

            if (TryReadImportAlias(line, out var alias))
            {
                importAliases.Add(alias);
            }
        }
    }

    private static bool TryReadIdentifierAfterKeyword(string line, string keyword, out string identifier)
    {
        identifier = string.Empty;

        if (!line.StartsWith(keyword, StringComparison.Ordinal)
            || line.Length <= keyword.Length
            || !char.IsWhiteSpace(line[keyword.Length]))
        {
            return false;
        }

        return TryReadIdentifier(line, keyword.Length, out identifier);
    }

    private static bool TryReadAssignmentTarget(string line, out string identifier)
    {
        identifier = string.Empty;
        var equalsIndex = line.IndexOf('=');

        if (equalsIndex <= 0 || (equalsIndex + 1 < line.Length && line[equalsIndex + 1] == '='))
        {
            return false;
        }

        var target = line[..equalsIndex].Trim();

        if (!IsValidIdentifier(target) || target is "var" or "global")
        {
            return false;
        }

        identifier = target;
        return true;
    }

    private static bool TryReadImportAlias(string line, out string alias)
    {
        alias = string.Empty;
        var marker = " as ";
        var index = line.IndexOf(marker, StringComparison.Ordinal);

        return index >= 0 && TryReadIdentifier(line, index + marker.Length, out alias);
    }

    private static void CollectParametersFromText(string line, ISet<string> variables)
    {
        var open = line.IndexOf('(');
        var close = line.IndexOf(')', open + 1);

        if (open < 0 || close < 0)
        {
            return;
        }

        foreach (var part in line.Substring(open + 1, close - open - 1).Split(','))
        {
            var parameter = part.Trim();

            if (IsValidIdentifier(parameter))
            {
                variables.Add(parameter);
            }
        }
    }

    private static bool TryReadIdentifier(string text, int start, out string identifier)
    {
        identifier = string.Empty;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start >= text.Length || !IsIdentifierStart(text[start]))
        {
            return false;
        }

        var end = start + 1;

        while (end < text.Length && IsIdentifierPart(text[end]))
        {
            end++;
        }

        identifier = text[start..end];
        return true;
    }

    private static bool IsValidIdentifier(string value)
    {
        return !string.IsNullOrEmpty(value)
            && IsIdentifierStart(value[0])
            && value.Skip(1).All(IsIdentifierPart);
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || char.IsDigit(value);

    private string ResolveImportPath(string path, string baseDirectory)
    {
        return ImportResolver.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));
    }

    private string ResolveExecuteFilePath(string path)
    {
        return ImportResolver.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(ResolveWorkingDirectory(), path));
    }

    private string ResolveWorkingDirectory()
    {
        return string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Environment.CurrentDirectory
            : ImportResolver.GetFullPath(WorkingDirectory);
    }

    private RuleScriptExecutionDirective NotifyRuntimeEvent(RuleScriptRuntimeEvent runtimeEvent)
    {
        var directive = RuntimeEventHandler?.Invoke(runtimeEvent) ?? RuleScriptExecutionDirective.Continue;

        if (runtimeEvent.Kind is RuleScriptRuntimeEventKind.BreakpointHit or RuleScriptRuntimeEventKind.StepPaused)
        {
            StepExecution = directive == RuleScriptExecutionDirective.StepOver;
        }

        return directive;
    }

    private async Task<RuleScriptExecutionDirective> NotifyRuntimeEventAsync(RuleScriptRuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        var directive = RuntimeEventHandlerAsync is not null
            ? await RuntimeEventHandlerAsync(runtimeEvent, cancellationToken).ConfigureAwait(false)
            : NotifyRuntimeEvent(runtimeEvent);

        if (runtimeEvent.Kind is RuleScriptRuntimeEventKind.BreakpointHit or RuleScriptRuntimeEventKind.StepPaused)
        {
            StepExecution = directive == RuleScriptExecutionDirective.StepOver;
        }

        return directive;
    }

    private void NotifyError(RuleScriptException exception)
    {
        var location = new RuleScriptSourceLocation(exception.SourceFile, exception.Line, exception.Column);
        NotifyRuntimeEvent(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.Error,
            location,
            exception.Message,
            exception: exception));
    }

    private Task NotifyErrorAsync(RuleScriptException exception, CancellationToken cancellationToken)
    {
        var location = new RuleScriptSourceLocation(exception.SourceFile, exception.Line, exception.Column);
        return NotifyRuntimeEventAsync(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.Error,
            location,
            exception.Message,
            exception: exception), cancellationToken);
    }

    private bool IsBreakpoint(RuleScriptSourceLocation location)
    {
        if (!location.Line.HasValue)
        {
            return false;
        }

        return _breakpoints.Any(breakpoint =>
            breakpoint.Line == location.Line.Value
            && (breakpoint.File is null || string.Equals(breakpoint.File, NormalizeLocationFile(location.File), StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssignSourceFile(RuleScriptException exception, string sourceFile)
    {
        exception.SourceFile ??= sourceFile;
    }

    private static string? NormalizeFile(string? file)
    {
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    private string? NormalizeBreakpointFile(string? file)
    {
        file = NormalizeFile(file);

        if (file is null || file == "<script>")
        {
            return file;
        }

        return ImportResolver.GetFullPath(Path.IsPathRooted(file)
            ? file
            : Path.Combine(ResolveWorkingDirectory(), file));
    }

    private string? NormalizeLocationFile(string? file)
    {
        file = NormalizeFile(file);

        if (file is null || file == "<script>")
        {
            return file;
        }

        return ImportResolver.GetFullPath(file);
    }
}
