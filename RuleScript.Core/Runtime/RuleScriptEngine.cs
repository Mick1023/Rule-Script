using RuleScript.Core.Diagnostics;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class RuleScriptEngine
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>> _asyncHostFunctions = new(StringComparer.Ordinal);
    private readonly List<RuleScriptBreakpoint> _breakpoints = [];
    private IImportResolver _importResolver = new FileSystemImportResolver();

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
    /// Notifies the host about runtime events. The returned directive controls step execution.
    /// </summary>
    public Func<RuleScriptRuntimeEvent, RuleScriptExecutionDirective>? RuntimeEventHandler { get; set; }

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

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            var module = BuildModule("<script>", statements, ResolveWorkingDirectory(), [], new(StringComparer.OrdinalIgnoreCase), isImported: false);
            new Interpreter(_builtinFunctions, _hostFunctions, _asyncHostFunctions, MaxLoopIterations, module, NotifyRuntimeEvent, IsBreakpoint, () => StepExecution).Execute(module, context);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, "<script>");
            NotifyError(exception);
            throw;
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

        try
        {
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            var module = BuildModule("<script>", statements, ResolveWorkingDirectory(), [], new(StringComparer.OrdinalIgnoreCase), isImported: false);
            await new Interpreter(_builtinFunctions, _hostFunctions, _asyncHostFunctions, MaxLoopIterations, module, NotifyRuntimeEvent, IsBreakpoint, () => StepExecution)
                .ExecuteAsync(module, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, "<script>");
            NotifyError(exception);
            throw;
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

        try
        {
            var module = LoadModule(fullPath, [], new(StringComparer.OrdinalIgnoreCase), originalPath: path, importingFile: null, isImported: false);
            new Interpreter(_builtinFunctions, _hostFunctions, _asyncHostFunctions, MaxLoopIterations, module, NotifyRuntimeEvent, IsBreakpoint, () => StepExecution).Execute(module, context);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, fullPath);
            NotifyError(exception);
            throw;
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

        try
        {
            var module = LoadModule(fullPath, [], new(StringComparer.OrdinalIgnoreCase), originalPath: path, importingFile: null, isImported: false);
            await new Interpreter(_builtinFunctions, _hostFunctions, _asyncHostFunctions, MaxLoopIterations, module, NotifyRuntimeEvent, IsBreakpoint, () => StepExecution)
                .ExecuteAsync(module, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RuleScriptException exception)
        {
            AssignSourceFile(exception, fullPath);
            NotifyError(exception);
            throw;
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

    private void NotifyError(RuleScriptException exception)
    {
        var location = new RuleScriptSourceLocation(exception.SourceFile, exception.Line, exception.Column);
        NotifyRuntimeEvent(new RuleScriptRuntimeEvent(
            RuleScriptRuntimeEventKind.Error,
            location,
            exception.Message,
            exception: exception));
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
