using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal sealed class RuleScriptNavigationAnalyzer
{
    private readonly RuleScriptEngine _engine;
    private readonly Dictionary<string, NavigationSymbol> _declarations = new(StringComparer.Ordinal);
    private readonly List<NavigationSymbol> _symbols = [];
    private readonly HashSet<string> _loadedImports = new(StringComparer.OrdinalIgnoreCase);
    private RuleScriptAnalysisResult _analysis = null!;

    private RuleScriptNavigationAnalyzer(RuleScriptEngine engine)
    {
        _engine = engine;
    }

    public static RuleScriptDefinitionInfo? GetDefinition(RuleScriptEngine engine, string source, int line, int column)
    {
        var analyzer = new RuleScriptNavigationAnalyzer(engine);
        analyzer.Analyze(source);
        var symbol = analyzer.FindSymbol(line, column);

        if (symbol is null)
        {
            return null;
        }

        if (symbol.IsExternal && analyzer._declarations.TryGetValue(symbol.Key, out var externalDeclaration))
        {
            return externalDeclaration.ToDefinition();
        }

        return analyzer._declarations.TryGetValue(symbol.Key, out var declaration)
            ? declaration.ToDefinition()
            : null;
    }

    public static IReadOnlyList<RuleScriptReferenceInfo> FindReferences(RuleScriptEngine engine, string source, int line, int column)
    {
        var analyzer = new RuleScriptNavigationAnalyzer(engine);
        analyzer.Analyze(source);
        var symbol = analyzer.FindSymbol(line, column);

        if (symbol is null)
        {
            return [];
        }

        var key = symbol.Key;
        return analyzer._symbols
            .Where(reference => string.Equals(reference.Key, key, StringComparison.Ordinal))
            .OrderBy(reference => reference.Range?.File ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Range?.StartLine ?? 0)
            .ThenBy(reference => reference.Range?.StartColumn ?? 0)
            .Select(reference => reference.ToReference())
            .ToArray();
    }

    private void Analyze(string source)
    {
        var tokens = new Lexer.Lexer(source).Tokenize();
        var statements = new Parser.Parser(tokens).Parse();
        _analysis = _engine.Analyze(source);

        foreach (var function in _analysis.Functions.Where(IsExternalFunction))
        {
            AddExternalFunction(function);
        }

        AddImportedDeclarations(statements, ResolveWorkingDirectory());
        VisitStatements(statements, new NavigationScope(null, isFunctionScope: false), file: null);
    }

    private void AddImportedDeclarations(IEnumerable<Statement> statements, string baseDirectory)
    {
        foreach (var import in statements.OfType<ImportStatement>())
        {
            var path = ResolveImportPath(import.Path, baseDirectory);

            if (!_engine.ImportResolver.Exists(path))
            {
                continue;
            }

            path = _engine.ImportResolver.GetFullPath(path);
            if (!_loadedImports.Add(path))
            {
                continue;
            }

            var importedSource = _engine.ImportResolver.ReadAllText(path);
            var importedStatements = new Parser.Parser(new Lexer.Lexer(importedSource).Tokenize()).Parse();
            AddImportedDeclarations(importedStatements, Path.GetDirectoryName(path) ?? baseDirectory);
            VisitStatements(importedStatements, new NavigationScope(null, isFunctionScope: false), path);

            foreach (var function in importedStatements.OfType<FunctionDeclarationStatement>().Where(function => function.IsExported))
            {
                var name = import.Alias is null ? function.Name : $"{import.Alias}.{function.Name}";
                var declaration = CreateFunctionDeclaration(name, function, path);
                AddDeclaration(declaration);
            }
        }
    }

    private void VisitStatements(IEnumerable<Statement> statements, NavigationScope scope, string? file)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclarationStatement function:
                    var declaration = CreateFunctionDeclaration(function.Name, function, file);
                    AddDeclaration(declaration);

                    var functionScope = new NavigationScope(scope, isFunctionScope: true);
                    foreach (var parameter in function.ParameterDefinitions)
                    {
                        if (TryCreateRange(file, parameter.Line, parameter.Column, parameter.Name, out var range))
                        {
                            var symbol = new NavigationSymbol(parameter.Name, RuleScriptSymbolKind.Parameter, range, range, IsDeclaration: true);
                            functionScope.Declare(symbol);
                            AddDeclaration(symbol);
                        }
                    }

                    VisitStatements(function.Body, functionScope, file);
                    break;
                case VarStatement variable:
                    AddVariableDeclaration(variable.Name, variable.Line, variable.Column, variable.Documentation, scope, file);
                    VisitExpression(variable.Initializer, scope, file);
                    break;
                case ConstStatement constant:
                    AddVariableDeclaration(constant.Name, constant.Line, constant.Column, constant.Documentation, scope, file);
                    VisitExpression(constant.Initializer, scope, file);
                    break;
                case AssignmentStatement assignment:
                    AddVariableUseOrDeclaration(assignment.Name, assignment.Line, assignment.Column, scope, file);
                    VisitExpression(assignment.Value, scope, file);
                    break;
                case GlobalAssignmentStatement globalAssignment:
                    AddGlobalVariableDeclaration(globalAssignment.Name, globalAssignment.Line, globalAssignment.Column, scope, file);
                    VisitExpression(globalAssignment.Value, scope, file);
                    break;
                case ExpressionStatement expression:
                    VisitExpression(expression.Expression, scope, file);
                    break;
                case ReturnStatement returnStatement:
                    VisitExpression(returnStatement.Value, scope, file);
                    break;
                case IfStatement ifStatement:
                    VisitExpression(ifStatement.Condition, scope, file);
                    VisitStatements(ifStatement.ThenBranch, scope, file);
                    VisitStatements(ifStatement.ElseBranch, scope, file);
                    break;
                case WhileStatement whileStatement:
                    VisitExpression(whileStatement.Condition, scope, file);
                    VisitStatements(whileStatement.Body, scope, file);
                    break;
                case ForeachStatement foreachStatement:
                    AddVariableDeclaration(foreachStatement.VariableName, foreachStatement.Line, foreachStatement.Column, null, scope, file);
                    VisitExpression(foreachStatement.Iterable, scope, file);
                    VisitStatements(foreachStatement.Body, scope, file);
                    break;
                case SwitchStatement switchStatement:
                    VisitExpression(switchStatement.Value, scope, file);
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        VisitStatements(switchCase.Body, scope, file);
                    }

                    if (switchStatement.DefaultBranch is not null)
                    {
                        VisitStatements(switchStatement.DefaultBranch, scope, file);
                    }

                    break;
                case TargetAssignmentStatement targetAssignment:
                    VisitExpression(targetAssignment.Target, scope, file);
                    VisitExpression(targetAssignment.Value, scope, file);
                    break;
                case DestructuringVarStatement destructuring:
                    VisitExpression(destructuring.Initializer, scope, file);
                    break;
            }
        }
    }

    private void VisitExpression(Expression? expression, NavigationScope scope, string? file)
    {
        switch (expression)
        {
            case null:
            case LiteralExpression:
                return;
            case IdentifierExpression identifier:
                AddVariableReference(identifier.Name, identifier.Line, identifier.Column, scope, file);
                return;
            case GlobalIdentifierExpression globalIdentifier:
                AddGlobalVariableReference(globalIdentifier.Name, globalIdentifier.Line, globalIdentifier.Column + 1, file);
                return;
            case FunctionCallExpression call:
                AddFunctionReference(call.Name, call.Line, call.Column, file);
                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, scope, file);
                }

                return;
            case ModuleFunctionCallExpression moduleCall:
                AddFunctionReference($"{moduleCall.ModuleName}.{moduleCall.FunctionName}", moduleCall.Line, moduleCall.Column + 1, file);
                foreach (var argument in moduleCall.Arguments)
                {
                    VisitExpression(argument, scope, file);
                }

                return;
            case BinaryExpression binary:
                VisitExpression(binary.Left, scope, file);
                VisitExpression(binary.Right, scope, file);
                return;
            case UnaryExpression unary:
                VisitExpression(unary.Operand, scope, file);
                return;
            case ArrayExpression array:
                foreach (var element in array.Elements)
                {
                    VisitExpression(element, scope, file);
                }

                return;
            case IndexExpression index:
                VisitExpression(index.Target, scope, file);
                VisitExpression(index.Index, scope, file);
                return;
            case MemberAccessExpression memberAccess:
                VisitExpression(memberAccess.Target, scope, file);
                return;
            case ConditionalMemberAccessExpression conditionalMemberAccess:
                VisitExpression(conditionalMemberAccess.Target, scope, file);
                return;
            case NullCoalescingExpression nullCoalescing:
                VisitExpression(nullCoalescing.Left, scope, file);
                VisitExpression(nullCoalescing.Right, scope, file);
                return;
            case ObjectLiteralExpression objectLiteral:
                foreach (var property in objectLiteral.Properties)
                {
                    VisitExpression(property.Value, scope, file);
                }

                return;
        }
    }

    private NavigationSymbol? FindSymbol(int line, int column)
    {
        return _symbols.LastOrDefault(symbol => symbol.SelectionRange is not null && Contains(symbol.SelectionRange, line, column));
    }

    private void AddFunctionReference(string name, int? line, int? column, string? file)
    {
        if (!TryCreateRange(file, line, column, name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name, out var range))
        {
            return;
        }

        var key = Key(RuleScriptSymbolKind.Function, name);
        if (_declarations.ContainsKey(key))
        {
            AddSymbol(new NavigationSymbol(name, RuleScriptSymbolKind.Function, range, range, IsDeclaration: false));
            return;
        }

        if (TryFindExternalFunction(name, out var externalFunction))
        {
            AddSymbol(externalFunction with { Range = range, SelectionRange = range, IsDeclaration = false });
        }
    }

    private void AddVariableUseOrDeclaration(string name, int? line, int? column, NavigationScope scope, string? file)
    {
        if (!TryCreateRange(file, line, column, name, out var range))
        {
            return;
        }

        if (scope.Resolve(name) is { } existing)
        {
            AddSymbol(existing with { Range = range, SelectionRange = range, IsDeclaration = false });
            return;
        }

        var declaration = new NavigationSymbol(name, RuleScriptSymbolKind.Variable, range, range, IsDeclaration: true);
        scope.Declare(declaration);
        AddDeclaration(declaration);
    }

    private void AddVariableDeclaration(
        string name,
        int? line,
        int? column,
        string? documentation,
        NavigationScope scope,
        string? file)
    {
        if (!TryCreateRange(file, line, column, name, out var range))
        {
            return;
        }

        var symbol = new NavigationSymbol(name, RuleScriptSymbolKind.Variable, range, range, IsDeclaration: true, documentation);
        scope.Declare(symbol);
        AddDeclaration(symbol);
    }

    private void AddGlobalVariableDeclaration(string name, int? line, int? column, NavigationScope scope, string? file)
    {
        if (!TryCreateRange(file, line, column, name, out var range))
        {
            return;
        }

        var root = scope.Root;
        var symbol = new NavigationSymbol(name, RuleScriptSymbolKind.Variable, range, range, IsDeclaration: true);
        root.Declare(symbol);
        AddDeclaration(symbol);
    }

    private void AddVariableReference(string name, int? line, int? column, NavigationScope scope, string? file)
    {
        if (scope.Resolve(name) is not { } declaration
            || !TryCreateRange(file, line, column, name, out var range))
        {
            return;
        }

        AddSymbol(declaration with { Range = range, SelectionRange = range, IsDeclaration = false });
    }

    private void AddGlobalVariableReference(string name, int? line, int? column, string? file)
    {
        var key = Key(RuleScriptSymbolKind.Variable, name);
        if (!_declarations.TryGetValue(key, out var declaration)
            || !TryCreateRange(file, line, column, name, out var range))
        {
            return;
        }

        AddSymbol(declaration with { Range = range, SelectionRange = range, IsDeclaration = false });
    }

    private void AddExternalFunction(RuleScriptFunctionSymbol function)
    {
        AddDeclaration(new NavigationSymbol(
            function.Name,
            ToNavigationKind(function.Kind),
            Range: null,
            SelectionRange: null,
            IsDeclaration: true,
            function.Documentation,
            IsExternal: true,
            function.Parameters,
            function.ReturnType));
    }

    private NavigationSymbol CreateFunctionDeclaration(string name, FunctionDeclarationStatement function, string? file)
    {
        var selectionRange = CreateRange(file, function.NameLine, function.NameColumn, function.Name);
        return new NavigationSymbol(
            name,
            RuleScriptSymbolKind.Function,
            ToRange(file, function.SourceSpan) ?? selectionRange,
            selectionRange,
            IsDeclaration: true,
            function.Documentation,
            Parameters: function.ParameterDefinitions.Select(parameter => new RuleScriptParameterSymbol(
                parameter.Name,
                parameter.TypeName is not null && RuleScriptTypeFacts.TryParse(parameter.TypeName, out var type)
                    ? type
                    : RuleScriptValueType.Unknown)).ToArray());
    }

    private void AddDeclaration(NavigationSymbol symbol)
    {
        _declarations[symbol.Key] = symbol;
        if (_symbols.Any(existing =>
                existing.IsDeclaration == symbol.IsDeclaration
                && string.Equals(existing.Key, symbol.Key, StringComparison.Ordinal)
                && Equals(existing.SelectionRange, symbol.SelectionRange)))
        {
            return;
        }

        AddSymbol(symbol);
    }

    private void AddSymbol(NavigationSymbol symbol)
    {
        _symbols.Add(symbol);
    }

    private string ResolveWorkingDirectory()
    {
        return string.IsNullOrWhiteSpace(_engine.WorkingDirectory)
            ? Environment.CurrentDirectory
            : _engine.ImportResolver.GetFullPath(_engine.WorkingDirectory);
    }

    private string ResolveImportPath(string path, string baseDirectory)
    {
        path = Path.HasExtension(path) ? path : path + _engine.ScriptFileExtension;
        return _engine.ImportResolver.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));
    }

    private static bool Contains(RuleScriptSourceRange range, int line, int column)
    {
        return (line > range.StartLine || (line == range.StartLine && column >= range.StartColumn))
            && (line < range.EndLine || (line == range.EndLine && column < range.EndColumn));
    }

    private static bool TryCreateRange(string? file, int? line, int? column, string name, out RuleScriptSourceRange range)
    {
        range = null!;
        if (line is null || column is null)
        {
            return false;
        }

        range = CreateRange(file, line, column, name);
        return true;
    }

    private static RuleScriptSourceRange CreateRange(string? file, int? line, int? column, string name)
    {
        var startLine = line ?? 1;
        var startColumn = column ?? 1;
        return new RuleScriptSourceRange(file, startLine, startColumn, startLine, startColumn + name.Length);
    }

    private static RuleScriptSourceRange? ToRange(string? file, SourceSpan? span)
    {
        return span is null
            ? null
            : new RuleScriptSourceRange(file, span.StartLine, span.StartColumn, span.EndLine, span.EndColumn);
    }

    private bool TryFindExternalFunction(string name, out NavigationSymbol function)
    {
        var symbolKind = _analysis.Functions
            .FirstOrDefault(function => string.Equals(function.Name, name, StringComparison.Ordinal) && IsExternalFunction(function))
            ?.Kind;

        if (symbolKind is not null
            && _declarations.TryGetValue(Key(ToNavigationKind(symbolKind.Value), name), out function!))
        {
            return true;
        }

        function = null!;
        return false;
    }

    private static bool IsExternalFunction(RuleScriptFunctionSymbol function)
    {
        return function.Kind is RuleScriptFunctionKind.Host or RuleScriptFunctionKind.Builtin;
    }

    private static RuleScriptSymbolKind ToNavigationKind(RuleScriptFunctionKind kind)
    {
        return kind switch
        {
            RuleScriptFunctionKind.User or RuleScriptFunctionKind.Imported => RuleScriptSymbolKind.Function,
            RuleScriptFunctionKind.Host or RuleScriptFunctionKind.Builtin => RuleScriptSymbolKind.HostFunction,
            _ => RuleScriptSymbolKind.Function
        };
    }

    private static string Key(RuleScriptSymbolKind kind, string name)
    {
        return $"{kind}:{name}";
    }

    private sealed class NavigationScope
    {
        private readonly Dictionary<string, NavigationSymbol> _symbols = new(StringComparer.Ordinal);

        public NavigationScope(NavigationScope? parent, bool isFunctionScope)
        {
            Parent = parent;
            IsFunctionScope = isFunctionScope;
        }

        public NavigationScope? Parent { get; }

        public bool IsFunctionScope { get; }

        public NavigationScope Root => Parent?.Root ?? this;

        public void Declare(NavigationSymbol symbol)
        {
            _symbols[symbol.Name] = symbol;
        }

        public NavigationSymbol? Resolve(string name)
        {
            return _symbols.TryGetValue(name, out var symbol)
                ? symbol
                : Parent?.Resolve(name);
        }
    }

    private sealed record NavigationSymbol(
        string Name,
        RuleScriptSymbolKind Kind,
        RuleScriptSourceRange? Range,
        RuleScriptSourceRange? SelectionRange,
        bool IsDeclaration,
        string? Documentation = null,
        bool IsExternal = false,
        IReadOnlyList<RuleScriptParameterSymbol>? Parameters = null,
        RuleScriptValueType ReturnType = RuleScriptValueType.Unknown)
    {
        public string Key => RuleScriptNavigationAnalyzer.Key(Kind, Name);

        public RuleScriptDefinitionInfo ToDefinition()
        {
            return new RuleScriptDefinitionInfo(
                Name,
                Kind,
                Range,
                SelectionRange,
                Documentation,
                IsExternal,
                Parameters,
                ReturnType);
        }

        public RuleScriptReferenceInfo ToReference()
        {
            return new RuleScriptReferenceInfo(Name, Kind, SelectionRange ?? Range, IsDeclaration, IsExternal);
        }
    }
}
