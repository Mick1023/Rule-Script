using RuleScript.Core.Diagnostics;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class RuleScriptEngine
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions = new(StringComparer.Ordinal);
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
    /// Removes a registered host function.
    /// </summary>
    public bool UnregisterFunction(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        return _hostFunctions.Remove(name);
    }

    /// <summary>
    /// Removes all registered host functions.
    /// </summary>
    public void ClearFunctions()
    {
        _hostFunctions.Clear();
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

        var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
        var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
        var module = BuildModule("<script>", statements, ResolveWorkingDirectory(), [], new(StringComparer.OrdinalIgnoreCase), isImported: false);
        new Interpreter(_builtinFunctions, _hostFunctions, MaxLoopIterations, module).Execute(module, context);
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
        var module = LoadModule(fullPath, [], new(StringComparer.OrdinalIgnoreCase), originalPath: path, importingFile: null, isImported: false);
        new Interpreter(_builtinFunctions, _hostFunctions, MaxLoopIterations, module).Execute(module, context);
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
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
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
}
