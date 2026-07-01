using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class RuleScriptEngine
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>> _asyncHostFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleScriptHostFunctionSymbol> _hostFunctionSignatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleScriptHostFunctionSymbol> _asyncHostFunctionSignatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleScriptValueType> _knownVariables = new(StringComparer.Ordinal);
    private readonly List<RuleScriptBreakpoint> _breakpoints = [];
    private readonly object _executionSync = new();
    private IImportResolver _importResolver = new FileSystemImportResolver();
    private string _scriptFileExtension = ".rules";
    private CancellationTokenSource? _stopCancellation;

    /// <summary>
    /// Gets or sets the maximum number of iterations allowed for each loop.
    /// </summary>
    public int MaxLoopIterations { get; set; } = 100000;

    /// <summary>
    /// Gets or sets whether <see cref="MaxLoopIterations"/> is enforced for while and foreach loops.
    /// Disable this only when the host provides another way to stop long-running execution.
    /// </summary>
    public bool LoopIterationLimitEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum elapsed execution time.
    /// </summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether <see cref="ExecutionTimeout"/> is enforced.
    /// </summary>
    public bool ExecutionTimeoutEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of active user-function calls.
    /// </summary>
    public int MaxCallDepth { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether <see cref="MaxCallDepth"/> is enforced.
    /// </summary>
    public bool CallDepthLimitEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of statements executed in one run.
    /// </summary>
    public long MaxExecutedStatements { get; set; } = 1000000;

    /// <summary>
    /// Gets or sets whether <see cref="MaxExecutedStatements"/> is enforced.
    /// </summary>
    public bool StatementExecutionLimitEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the base directory used for relative <see cref="ExecuteFile(string)"/> paths.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the file extension appended to extensionless script and import paths.
    /// </summary>
    public string ScriptFileExtension
    {
        get => _scriptFileExtension;
        set => _scriptFileExtension = NormalizeScriptFileExtension(value);
    }

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
    /// Gets typed host function signatures registered on the engine.
    /// </summary>
    public IReadOnlyList<RuleScriptHostFunctionSymbol> RegisteredHostFunctions =>
        _hostFunctionSignatures.Values
            .Concat(_asyncHostFunctionSignatures.Values)
            .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.IsAsync)
            .ToArray();

    /// <summary>
    /// Gets variables supplied by the host for semantic analysis.
    /// </summary>
    public IReadOnlyList<RuleScriptVariableSymbol> KnownVariables =>
        _knownVariables
            .Select(variable => new RuleScriptVariableSymbol(variable.Key, variable.Value))
            .OrderBy(variable => variable.Name, StringComparer.Ordinal)
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
        _hostFunctionSignatures.Remove(name);
    }

    /// <summary>
    /// Registers or replaces a typed host function.
    /// </summary>
    public void RegisterFunction(
        string name,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        Func<IReadOnlyList<object?>, object?> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var signature = CreateHostFunctionSignature(name, parameters, returnType, isAsync: false);
        _hostFunctions[name] = function;
        _hostFunctionSignatures[name] = signature;
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
        _asyncHostFunctionSignatures.Remove(name);
    }

    /// <summary>
    /// Registers or replaces a typed async host function.
    /// </summary>
    public void RegisterFunctionAsync(
        string name,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        Func<IReadOnlyList<object?>, CancellationToken, Task<object?>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var signature = CreateHostFunctionSignature(name, parameters, returnType, isAsync: true);
        _asyncHostFunctions[name] = function;
        _asyncHostFunctionSignatures[name] = signature;
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
    /// Registers or replaces a typed async host function.
    /// </summary>
    public void RegisterFunctionAsync(
        string name,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        Func<IReadOnlyList<object?>, Task<object?>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        RegisterFunctionAsync(name, parameters, returnType, (args, _) => function(args));
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
        var asyncRemoved = _asyncHostFunctions.Remove(name);
        _hostFunctionSignatures.Remove(name);
        _asyncHostFunctionSignatures.Remove(name);
        return asyncRemoved || removed;
    }

    /// <summary>
    /// Removes all registered host functions.
    /// </summary>
    public void ClearFunctions()
    {
        _hostFunctions.Clear();
        _asyncHostFunctions.Clear();
        _hostFunctionSignatures.Clear();
        _asyncHostFunctionSignatures.Clear();
    }

    /// <summary>
    /// Adds or replaces a host-provided variable for semantic analysis.
    /// </summary>
    public void SetKnownVariable(string name, RuleScriptValueType type = RuleScriptValueType.Unknown)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Variable name cannot be empty.", nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        _knownVariables[name] = type;
    }

    /// <summary>
    /// Removes a host-provided variable from semantic analysis.
    /// </summary>
    public bool RemoveKnownVariable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Variable name cannot be empty.", nameof(name));
        }

        return _knownVariables.Remove(name);
    }

    /// <summary>
    /// Removes all host-provided variables from semantic analysis.
    /// </summary>
    public void ClearKnownVariables()
    {
        _knownVariables.Clear();
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
    /// Strictly analyzes parseable script text and available imports without executing them, then returns symbols for editor tooling.
    /// Syntax errors are reported by throwing <see cref="SyntaxException"/>.
    /// </summary>
    public RuleScriptAnalysisResult Analyze(string script)
    {
        return AnalyzeCore(script, cursorLine: null, cursorColumn: null);
    }

    /// <summary>
    /// Analyzes script text and returns variables visible at the specified cursor position.
    /// </summary>
    public RuleScriptAnalysisResult Analyze(string script, int line, int column)
    {
        ValidateCursor(line, column);
        return AnalyzeCore(script, line, column);
    }

    private RuleScriptAnalysisResult AnalyzeCore(string script, int? cursorLine, int? cursorColumn)
    {
        ArgumentNullException.ThrowIfNull(script);

        var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
        var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var userFunctions = new HashSet<string>(StringComparer.Ordinal);
        var importAliases = new HashSet<string>(StringComparer.Ordinal);

        CollectSymbols(statements, variables, userFunctions, importAliases);
        CollectImportedFunctionSymbols(
            statements,
            ResolveWorkingDirectory(),
            userFunctions,
            new Stack<string>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

        return CreateAnalysisResult(
            statements,
            variables,
            userFunctions,
            importAliases,
            cursorLine,
            cursorColumn);
    }

    /// <summary>
    /// Best-effort analyzes script text and available imports without executing them, returning diagnostics and partial symbols instead of throwing for syntax errors.
    /// </summary>
    public RuleScriptAnalysisAttempt TryAnalyze(string script)
    {
        return TryAnalyzeCore(script, cursorLine: null, cursorColumn: null);
    }

    /// <summary>
    /// Best-effort analyzes script text and returns variables visible at the specified cursor position.
    /// </summary>
    public RuleScriptAnalysisAttempt TryAnalyze(string script, int line, int column)
    {
        ValidateCursor(line, column);
        return TryAnalyzeCore(script, line, column);
    }

    private RuleScriptAnalysisAttempt TryAnalyzeCore(string script, int? cursorLine, int? cursorColumn)
    {
        ArgumentNullException.ThrowIfNull(script);

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var parseResult = new RuleScript.Core.Parser.Parser(tokens).ParseWithDiagnostics();
            var variables = new HashSet<string>(StringComparer.Ordinal);
            var userFunctions = new HashSet<string>(StringComparer.Ordinal);
            var importAliases = new HashSet<string>(StringComparer.Ordinal);

            CollectSymbols(parseResult.Statements, variables, userFunctions, importAliases);
            CollectSymbolsFromTokens(tokens, variables, userFunctions, importAliases);
            CollectImportedFunctionSymbolsFromTokens(tokens, userFunctions);

            var symbols = CreateAnalysisResult(
                parseResult.Statements,
                variables,
                userFunctions,
                importAliases,
                cursorLine,
                cursorColumn,
                fallbackVisibleToAll: !parseResult.Success);
            var diagnostics = parseResult.Diagnostics
                .Select(CreateDiagnostic)
                .Concat(symbols.Diagnostics)
                .ToArray();
            var success = parseResult.Success
                && diagnostics.All(diagnostic => diagnostic.Severity != RuleScriptDiagnosticSeverity.Error);

            return new RuleScriptAnalysisAttempt(symbols, diagnostics, success);
        }
        catch (SyntaxException exception)
        {
            return new RuleScriptAnalysisAttempt(AnalyzeBestEffort(script), [CreateDiagnostic(exception)], success: false);
        }
    }

    private static void ValidateCursor(int line, int column)
    {
        if (line <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Cursor line must be greater than zero.");
        }

        if (column <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Cursor column must be greater than zero.");
        }
    }

    private static RuleScriptDiagnostic CreateDiagnostic(SyntaxException exception)
    {
        RuleScriptSourceRange? range = null;

        if (exception.Line.HasValue && exception.Column.HasValue)
        {
            var endLine = exception.EndLine ?? exception.Line.Value;
            var endColumn = exception.EndColumn
                ?? exception.Column.Value + Math.Max(exception.TokenText?.Length ?? 0, 1);
            range = new RuleScriptSourceRange(
                exception.SourceFile,
                exception.Line.Value,
                exception.Column.Value,
                endLine,
                endColumn);
        }

        var code = exception.Message.Contains("Duplicate parameter name", StringComparison.Ordinal)
            ? RuleScriptDiagnosticCodes.DuplicateParameter
            : RuleScriptDiagnosticCodes.SyntaxError;

        return new RuleScriptDiagnostic(
            exception.Message,
            exception.Line,
            exception.Column,
            exception.TokenText,
            exception.SourceFile,
            range)
        {
            Code = code,
            Severity = RuleScriptDiagnosticSeverity.Error
        };
    }

    private RuleScriptAnalysisResult CreateAnalysisResult(
        IReadOnlyList<Statement> statements,
        IEnumerable<string> variableNames,
        IEnumerable<string> userFunctionNames,
        IEnumerable<string> importAliases,
        int? cursorLine,
        int? cursorColumn,
        bool fallbackVisibleToAll = false)
    {
        var registeredHostFunctions = RegisteredHostFunctions;
        var builtinFunctions = _builtinFunctions.Signatures;
        var hostReturnTypes = registeredHostFunctions
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().ReturnType,
                StringComparer.Ordinal);
        foreach (var builtinFunction in builtinFunctions)
        {
            hostReturnTypes.TryAdd(builtinFunction.Name, builtinFunction.ReturnType);
        }
        var typedSymbols = RuleScriptSymbolAnalyzer.Analyze(statements, cursorLine, cursorColumn, hostReturnTypes, _knownVariables);
        var functionSymbols = typedSymbols.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        CollectImportedFunctionSignatures(
            statements,
            ResolveWorkingDirectory(),
            functionSymbols,
            new Stack<string>(),
            new Dictionary<string, IReadOnlyList<RuleScriptFunctionSymbol>>(StringComparer.OrdinalIgnoreCase));
        IEnumerable<RuleScriptVariableSymbol>? visibleVariables = typedSymbols.VisibleVariables;

        if (fallbackVisibleToAll && cursorLine.HasValue && cursorColumn.HasValue)
        {
            visibleVariables = typedSymbols.Variables.Concat(
                variableNames.Select(name => new RuleScriptVariableSymbol(name, RuleScriptValueType.Unknown)));
        }

        var availableFunctions = new HashSet<string>(userFunctionNames, StringComparer.Ordinal);
        availableFunctions.UnionWith(functionSymbols.Keys);
        availableFunctions.UnionWith(RegisteredFunctionNames);
        availableFunctions.UnionWith(_builtinFunctions.Names);
        var hostSymbols = registeredHostFunctions
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var builtinFunction in builtinFunctions)
        {
            hostSymbols.TryAdd(
                builtinFunction.Name,
                new RuleScriptHostFunctionSymbol(
                    builtinFunction.Name,
                    builtinFunction.Parameters,
                    builtinFunction.ReturnType));
        }
        var semanticDiagnostics = RuleScriptSemanticAnalyzer.Analyze(
            statements,
            _knownVariables,
            availableFunctions,
            functionSymbols,
            hostSymbols)
            .Concat(typedSymbols.Diagnostics)
            .ToArray();

        return new RuleScriptAnalysisResult(
            variableNames,
            userFunctionNames,
            RegisteredFunctionNames,
            _builtinFunctions.Names,
            importAliases,
            typedSymbols.Variables,
            functionSymbols.Values,
            visibleVariables,
            registeredHostFunctions,
            semanticDiagnostics,
            builtinFunctions);
    }

    private void CollectImportedFunctionSignatures(
        IEnumerable<Statement> statements,
        string baseDirectory,
        IDictionary<string, RuleScriptFunctionSymbol> functions,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<RuleScriptFunctionSymbol>> moduleCache)
    {
        foreach (var import in statements.OfType<ImportStatement>())
        {
            var path = ResolveImportPath(import.Path, baseDirectory);

            if (!ImportResolver.Exists(path))
            {
                continue;
            }

            IReadOnlyList<RuleScriptFunctionSymbol> importedFunctions;

            try
            {
                importedFunctions = LoadImportedFunctionSignatures(path, importStack, moduleCache);
            }
            catch (RuleScriptException)
            {
                continue;
            }

            foreach (var function in importedFunctions)
            {
                var name = import.Alias is null ? function.Name : $"{import.Alias}.{function.Name}";
                functions[name] = new RuleScriptFunctionSymbol(
                    name,
                    function.Parameters,
                    function.ReturnType,
                    function.IsReturnTypeNullable);
            }
        }
    }

    private IReadOnlyList<RuleScriptFunctionSymbol> LoadImportedFunctionSignatures(
        string path,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<RuleScriptFunctionSymbol>> moduleCache)
    {
        path = ImportResolver.GetFullPath(path);

        if (moduleCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (importStack.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        importStack.Push(path);

        try
        {
            var script = ImportResolver.ReadAllText(path);
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            var functions = new Dictionary<string, RuleScriptFunctionSymbol>(StringComparer.Ordinal);
            var baseDirectory = Path.GetDirectoryName(path) ?? ResolveWorkingDirectory();

            foreach (var import in statements.OfType<ImportStatement>().Where(value => value.Alias is null))
            {
                var nestedPath = ResolveImportPath(import.Path, baseDirectory);

                if (!ImportResolver.Exists(nestedPath))
                {
                    continue;
                }

                foreach (var function in LoadImportedFunctionSignatures(nestedPath, importStack, moduleCache))
                {
                    functions[function.Name] = function;
                }
            }

            var analyzedFunctions = RuleScriptSymbolAnalyzer.Analyze(statements, null, null).Functions;
            foreach (var function in analyzedFunctions)
            {
                functions[function.Name] = function;
            }

            var snapshot = functions.Values.OrderBy(function => function.Name, StringComparer.Ordinal).ToArray();
            moduleCache[path] = snapshot;
            return snapshot;
        }
        finally
        {
            importStack.Pop();
        }
    }

    private static RuleScriptFunctionSymbol CreateFunctionSymbol(FunctionDeclarationStatement declaration)
    {
        var parameters = declaration.ParameterDefinitions.Select(parameter =>
        {
            var type = parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var parsed)
                ? parsed
                : RuleScriptValueType.Unknown;
            return new RuleScriptParameterSymbol(parameter.Name, type);
        });
        return new RuleScriptFunctionSymbol(declaration.Name, parameters);
    }

    /// <summary>
    /// Adds a breakpoint for any source file at the specified line.
    /// </summary>
    public void AddBreakpoint(int line)
    {
        AddBreakpoint(null, line);
    }

    /// <summary>
    /// Adds a conditional breakpoint for any source file at the specified line.
    /// </summary>
    public void AddBreakpoint(int line, string condition)
    {
        AddBreakpoint(null, line, condition);
    }

    /// <summary>
    /// Adds a breakpoint for the specified source file and line.
    /// </summary>
    public void AddBreakpoint(string? file, int line)
    {
        AddBreakpoint(file, line, condition: null);
    }

    /// <summary>
    /// Adds a conditional breakpoint for the specified source file and line.
    /// </summary>
    public void AddBreakpoint(string? file, int line, string? condition)
    {
        if (line <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Breakpoint line must be greater than zero.");
        }

        condition = NormalizeBreakpointCondition(condition);
        var breakpoint = new RuleScriptBreakpoint(NormalizeBreakpointFile(file), line, condition);

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
        var normalizedFile = NormalizeBreakpointFile(file);
        return _breakpoints.RemoveAll(value =>
            value.Line == line && string.Equals(value.File, normalizedFile, StringComparison.OrdinalIgnoreCase)) > 0;
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
            _hostFunctionSignatures,
            _asyncHostFunctionSignatures,
            MaxLoopIterations,
            LoopIterationLimitEnabled,
            ExecutionTimeout,
            ExecutionTimeoutEnabled,
            MaxCallDepth,
            CallDepthLimitEnabled,
            MaxExecutedStatements,
            StatementExecutionLimitEnabled,
            module,
            NotifyRuntimeEvent,
            NotifyRuntimeEventAsync,
            GetBreakpoints,
            () => StepExecution,
            cancellationToken);
    }

    private static RuleScriptHostFunctionSymbol CreateHostFunctionSignature(
        string name,
        IReadOnlyList<RuleScriptParameterSymbol> parameters,
        RuleScriptValueType returnType,
        bool isAsync)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(parameters);

        if (returnType == RuleScriptValueType.Unknown || !Enum.IsDefined(returnType))
        {
            throw new ArgumentException("Host function return type must be a known RuleScript type or Any.", nameof(returnType));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
            {
                throw new ArgumentException("Host function parameter names cannot be empty.", nameof(parameters));
            }

            if (!names.Add(parameter.Name))
            {
                throw new ArgumentException($"Duplicate host function parameter name '{parameter.Name}'.", nameof(parameters));
            }

            if (parameter.Type == RuleScriptValueType.Unknown || !Enum.IsDefined(parameter.Type))
            {
                throw new ArgumentException($"Host function parameter '{parameter.Name}' must use a known RuleScript type or Any.", nameof(parameters));
            }
        }

        return new RuleScriptHostFunctionSymbol(name, parameters, returnType, isAsync);
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
                case DestructuringVarStatement destructuring:
                    foreach (var name in destructuring.Pattern.Names)
                    {
                        variables.Add(name);
                    }
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
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectSymbols(switchCase.Body, variables, userFunctions, importAliases);
                    }

                    if (switchStatement.DefaultBranch is not null)
                    {
                        CollectSymbols(switchStatement.DefaultBranch, variables, userFunctions, importAliases);
                    }

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

    private void CollectImportedFunctionSymbols(
        IEnumerable<Statement> statements,
        string baseDirectory,
        ISet<string> userFunctions,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<string>> moduleCache)
    {
        foreach (var import in statements.OfType<ImportStatement>())
        {
            CollectImportedFunctionSymbols(import.Path, import.Alias, baseDirectory, userFunctions, importStack, moduleCache);
        }
    }

    private void CollectImportedFunctionSymbols(
        string path,
        string? alias,
        string baseDirectory,
        ISet<string> userFunctions,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<string>> moduleCache)
    {
        var importPath = ResolveImportPath(path, baseDirectory);

        // Analysis is also used while a project is being edited. Keep symbols from
        // the current buffer available even when an imported file does not exist yet.
        if (!ImportResolver.Exists(importPath))
        {
            return;
        }

        var importedFunctions = LoadImportedFunctionSymbols(importPath, importStack, moduleCache);

        foreach (var function in importedFunctions)
        {
            userFunctions.Add(alias is null ? function : $"{alias}.{function}");
        }
    }

    private IReadOnlyList<string> LoadImportedFunctionSymbols(
        string path,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<string>> moduleCache)
    {
        path = ImportResolver.GetFullPath(path);

        if (moduleCache.TryGetValue(path, out var cachedFunctions))
        {
            return cachedFunctions;
        }

        // Execution reports circular imports. Static analysis only needs a finite,
        // best-effort symbol set, so stop walking a cycle at the repeated module.
        if (importStack.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        importStack.Push(path);

        try
        {
            IReadOnlyList<Statement> statements;

            try
            {
                var script = ImportResolver.ReadAllText(path);
                var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
                statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            }
            catch (RuleScriptException exception)
            {
                AssignSourceFile(exception, path);
                throw;
            }

            var functions = new HashSet<string>(StringComparer.Ordinal);
            var baseDirectory = Path.GetDirectoryName(path) ?? ResolveWorkingDirectory();

            foreach (var import in statements.OfType<ImportStatement>().Where(value => value.Alias is null))
            {
                var nestedPath = ResolveImportPath(import.Path, baseDirectory);

                if (ImportResolver.Exists(nestedPath))
                {
                    functions.UnionWith(LoadImportedFunctionSymbols(nestedPath, importStack, moduleCache));
                }
            }

            functions.UnionWith(statements.OfType<FunctionDeclarationStatement>().Select(function => function.Name));

            var snapshot = functions.Order(StringComparer.Ordinal).ToArray();
            moduleCache[path] = snapshot;
            return snapshot;
        }
        finally
        {
            importStack.Pop();
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
            CollectImportedFunctionSymbolsFromTokens(tokens, userFunctions);
        }
        catch (SyntaxException)
        {
            CollectSymbolsFromText(script, variables, userFunctions, importAliases);
            CollectImportedFunctionSymbolsFromText(script, userFunctions);
        }

        return new RuleScriptAnalysisResult(
            variables,
            userFunctions,
            RegisteredFunctionNames,
            _builtinFunctions.Names,
            importAliases);
    }

    private void CollectImportedFunctionSymbolsFromTokens(IReadOnlyList<Token> tokens, ISet<string> userFunctions)
    {
        var importStack = new Stack<string>();
        var moduleCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.Import
                || !TryGetToken(tokens, i + 1, out var pathToken)
                || pathToken.Type != TokenType.String)
            {
                continue;
            }

            string? alias = null;

            for (var j = i + 2; j < tokens.Count && tokens[j].Type is not TokenType.Semicolon and not TokenType.EndOfFile; j++)
            {
                if (tokens[j].Type == TokenType.As && TryGetIdentifier(tokens, j + 1, out var name))
                {
                    alias = name;
                    break;
                }
            }

            TryCollectImportedFunctionSymbols(pathToken.Literal?.ToString() ?? pathToken.Lexeme, alias, userFunctions, importStack, moduleCache);
        }
    }

    private void CollectImportedFunctionSymbolsFromText(string script, ISet<string> userFunctions)
    {
        var importStack = new Stack<string>();
        var moduleCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in script.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();

            if (!line.StartsWith("import", StringComparison.Ordinal)
                || line.Length <= "import".Length
                || !char.IsWhiteSpace(line["import".Length]))
            {
                continue;
            }

            var quoteStart = line.IndexOf('"', "import".Length);
            var quoteEnd = quoteStart < 0 ? -1 : line.IndexOf('"', quoteStart + 1);

            if (quoteStart < 0 || quoteEnd < 0)
            {
                continue;
            }

            var path = line[(quoteStart + 1)..quoteEnd];
            var suffix = line[(quoteEnd + 1)..].TrimStart();
            var alias = TryReadIdentifierAfterKeyword(suffix, "as", out var name) ? name : null;
            TryCollectImportedFunctionSymbols(path, alias, userFunctions, importStack, moduleCache);
        }
    }

    private void TryCollectImportedFunctionSymbols(
        string path,
        string? alias,
        ISet<string> userFunctions,
        Stack<string> importStack,
        IDictionary<string, IReadOnlyList<string>> moduleCache)
    {
        try
        {
            CollectImportedFunctionSymbols(path, alias, ResolveWorkingDirectory(), userFunctions, importStack, moduleCache);
        }
        catch (RuleScriptException)
        {
            // The main parse diagnostic remains the useful result for best-effort analysis.
        }
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

        var expectName = true;

        for (var i = start + 1; i < tokens.Count && tokens[i].Type is not TokenType.RightParen and not TokenType.EndOfFile; i++)
        {
            if (expectName && tokens[i].Type == TokenType.Identifier)
            {
                variables.Add(tokens[i].Lexeme);
                expectName = false;
            }
            else if (tokens[i].Type == TokenType.Comma)
            {
                expectName = true;
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
            var parameter = part.Split(':', 2)[0].Trim();

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
        path = AppendScriptFileExtension(path);
        return ImportResolver.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));
    }

    private string ResolveExecuteFilePath(string path)
    {
        path = AppendScriptFileExtension(path);
        return ImportResolver.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(ResolveWorkingDirectory(), path));
    }

    private string AppendScriptFileExtension(string path)
    {
        return Path.HasExtension(path) ? path : path + ScriptFileExtension;
    }

    private static string NormalizeScriptFileExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Script file extension cannot be empty.", nameof(value));
        }

        value = value.Trim();

        if (value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("Script file extension cannot contain directory separators.", nameof(value));
        }

        return value.StartsWith('.') ? value : $".{value}";
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

    private IReadOnlyList<RuleScriptBreakpoint> GetBreakpoints(RuleScriptSourceLocation location)
    {
        if (!location.Line.HasValue)
        {
            return [];
        }

        return _breakpoints.Where(breakpoint =>
                breakpoint.Line == location.Line.Value
                && (breakpoint.File is null || string.Equals(breakpoint.File, NormalizeLocationFile(location.File), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string? NormalizeBreakpointCondition(string? condition)
    {
        if (condition is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(condition))
        {
            throw new ArgumentException("Breakpoint condition cannot be empty.", nameof(condition));
        }

        var normalized = condition.Trim();
        var tokens = new RuleScript.Core.Lexer.Lexer($"{normalized};").Tokenize();
        var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();

        if (statements.Count != 1 || statements[0] is not ExpressionStatement)
        {
            throw new ArgumentException("Breakpoint condition must be a single expression.", nameof(condition));
        }

        return normalized;
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
