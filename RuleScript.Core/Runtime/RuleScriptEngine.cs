using RuleScript.Core.Diagnostics;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

public sealed class RuleScriptEngine
{
    private readonly BuiltinFunctions _builtinFunctions;
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _hostFunctions = new(StringComparer.Ordinal);

    public int MaxLoopIterations { get; set; } = 100000;

    public RuleScriptEngine()
        : this(new BuiltinFunctions())
    {
    }

    public RuleScriptEngine(BuiltinFunctions builtinFunctions)
    {
        _builtinFunctions = builtinFunctions ?? throw new ArgumentNullException(nameof(builtinFunctions));
    }

    public void RegisterFunction(string name, Func<IReadOnlyList<object?>, object?> function)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        _hostFunctions[name] = function ?? throw new ArgumentNullException(nameof(function));
    }

    public bool UnregisterFunction(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name cannot be empty.", nameof(name));
        }

        return _hostFunctions.Remove(name);
    }

    public void ClearFunctions()
    {
        _hostFunctions.Clear();
    }

    public RuntimeContext Execute(string script)
    {
        var context = new RuntimeContext();
        Execute(script, context);
        return context;
    }

    public void Execute(string script, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
        var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
        var module = BuildModule("<script>", statements, Directory.GetCurrentDirectory(), []);
        new Interpreter(_builtinFunctions, _hostFunctions, MaxLoopIterations, module).Execute(module, context);
    }

    public RuntimeContext ExecuteFile(string path)
    {
        var context = new RuntimeContext();
        ExecuteFile(path, context);
        return context;
    }

    public void ExecuteFile(string path, RuntimeContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Script path cannot be empty.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(context);

        var fullPath = Path.GetFullPath(path);
        var module = LoadModule(fullPath, []);
        new Interpreter(_builtinFunctions, _hostFunctions, MaxLoopIterations, module).Execute(module, context);
    }

    private ScriptModule LoadModule(string path, Stack<string> importStack)
    {
        if (importStack.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            throw new RuntimeException($"Circular import detected for '{path}'.");
        }

        if (!File.Exists(path))
        {
            throw new RuntimeException($"Import file '{path}' was not found.");
        }

        importStack.Push(path);

        try
        {
            var script = File.ReadAllText(path);
            var tokens = new RuleScript.Core.Lexer.Lexer(script).Tokenize();
            var statements = new RuleScript.Core.Parser.Parser(tokens).Parse();
            return BuildModule(path, statements, Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(), importStack);
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
        Stack<string> importStack)
    {
        var module = new ScriptModule(name, statements);

        foreach (var import in statements.OfType<ImportStatement>())
        {
            var importPath = ResolveImportPath(import.Path, baseDirectory);
            var importedModule = LoadModule(importPath, importStack);

            if (import.Alias is not null)
            {
                if (module.Aliases.ContainsKey(import.Alias))
                {
                    throw new RuntimeException($"Duplicate import alias '{import.Alias}'.", import.Line, import.Column, import.Alias);
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

    private static string ResolveImportPath(string path, string baseDirectory)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));
    }
}
