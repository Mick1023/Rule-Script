using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal sealed class RuleScriptNavigationAnalyzer
{
    private readonly RuleScriptDocumentAnalysisResult _document;
    private readonly RuleScriptEngine _engine;
    private readonly Dictionary<string, NavigationSymbol> _declarations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<NavigationSymbol>> _functionOverloads = new(StringComparer.Ordinal);
    private readonly List<NavigationSymbol> _symbols = [];
    private readonly HashSet<string> _loadedImports = new(StringComparer.OrdinalIgnoreCase);

    private RuleScriptNavigationAnalyzer(RuleScriptDocumentAnalysisResult document)
    {
        _document = document;
        _engine = document.Engine;
    }

    public static RuleScriptDefinitionInfo? GetDefinition(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var analyzer = new RuleScriptNavigationAnalyzer(document);
        analyzer.Analyze();
        var symbol = analyzer.FindSymbol(line, column);

        if (symbol is null)
        {
            return null;
        }

        return analyzer.ResolveDeclaration(symbol)?.ToDefinition();
    }

    public static IReadOnlyList<RuleScriptReferenceInfo> FindReferences(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var analyzer = new RuleScriptNavigationAnalyzer(document);
        analyzer.Analyze();
        var symbol = analyzer.FindSymbol(line, column);

        if (symbol is null)
        {
            return [];
        }

        var key = symbol.Key;
        var declaration = analyzer.ResolveDeclaration(symbol);
        var isOverloaded = analyzer._functionOverloads.TryGetValue(key, out var overloads) && overloads.Count > 1;
        return analyzer._symbols
            .Where(reference => string.Equals(reference.Key, key, StringComparison.Ordinal))
            .Where(reference => !isOverloaded || ReferenceEquals(analyzer.ResolveDeclaration(reference), declaration))
            .OrderBy(reference => reference.Range?.File ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Range?.StartLine ?? 0)
            .ThenBy(reference => reference.Range?.StartColumn ?? 0)
            .Select(reference => reference.ToReference())
            .ToArray();
    }

    public static RuleScriptHoverInfo? GetHover(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var analyzer = new RuleScriptNavigationAnalyzer(document);
        analyzer.Analyze();
        var symbol = analyzer.FindSymbol(line, column);

        if (symbol is null)
        {
            return null;
        }

        var declaration = analyzer.ResolveDeclaration(symbol) ?? symbol;
        return analyzer.CreateHover(declaration, symbol.SelectionRange ?? symbol.Range, symbol);
    }

    /// <summary>
    /// Overloads share one declaration key, so a function declaration resolves to itself and a call picks the overload
    /// the semantic analyzer resolved, then one matching its argument count, before falling back to the last declaration
    /// registered under the key.
    /// </summary>
    private NavigationSymbol? ResolveDeclaration(NavigationSymbol symbol)
    {
        if (IsFunctionDeclaration(symbol))
        {
            return symbol;
        }

        if (_functionOverloads.TryGetValue(symbol.Key, out var overloads))
        {
            if (symbol.ResolvedFunction is { } resolved
                && overloads.FirstOrDefault(overload => HasSignature(overload, resolved.Parameters)) is { } exact)
            {
                return exact;
            }

            if (symbol.ArgumentCount is { } count
                && overloads.FirstOrDefault(overload => overload.Parameters?.Count == count) is { } byCount)
            {
                return byCount;
            }
        }

        return _declarations.GetValueOrDefault(symbol.Key);
    }

    private static bool IsFunctionDeclaration(NavigationSymbol symbol)
    {
        return symbol.IsDeclaration && symbol.Kind is RuleScriptSymbolKind.Function or RuleScriptSymbolKind.HostFunction;
    }

    private static bool HasSignature(NavigationSymbol declaration, IReadOnlyList<RuleScriptParameterSymbol> parameters)
    {
        return declaration.Parameters is not null
            && string.Equals(
                RuleScriptFunctionSymbol.CreateSignatureKey(declaration.Name, declaration.Parameters),
                RuleScriptFunctionSymbol.CreateSignatureKey(declaration.Name, parameters),
                StringComparison.Ordinal);
    }

    private void Analyze()
    {
        foreach (var function in _document.Analysis.Functions.Where(IsExternalFunction))
        {
            AddExternalFunction(function);
        }

        AddImportedDeclarations(_document.Statements, _document.BaseDirectory ?? ResolveWorkingDirectory());
        VisitStatements(_document.Statements, new NavigationScope(null, isFunctionScope: false), file: null);
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
            // Imported files may be mid-edit too, so navigation keeps whatever parses instead of failing the whole document.
            var importedTokens = new Lexer.Lexer(importedSource).TokenizeWithDiagnostics([]);
            var importedStatements = new Parser.Parser(importedTokens).ParseWithDiagnostics().Statements;
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
                            var symbol = new NavigationSymbol(parameter.Name, RuleScriptSymbolKind.Parameter, range, range, IsDeclaration: true)
                            {
                                ValueType = ParseTypeName(parameter.TypeName)
                            };
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
                    AddVariableDeclaration(
                        foreachStatement.VariableName,
                        foreachStatement.VariableLine ?? foreachStatement.Line,
                        foreachStatement.VariableColumn ?? foreachStatement.Column,
                        null,
                        scope,
                        file);
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
                case CompoundAssignmentStatement compoundAssignment:
                    VisitExpression(compoundAssignment.Target, scope, file);
                    VisitExpression(compoundAssignment.Value, scope, file);
                    break;
                case DestructuringVarStatement destructuring:
                    VisitExpression(destructuring.Initializer, scope, file);
                    break;
                case ParallelStatementSyntax parallel:
                    VisitTasks(parallel.Tasks, scope, file);
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
                AddFunctionReference(call.Name, call.Line, call.Column, call.Arguments.Count, FindResolvedCall(call.Line, call.Column, file), file);
                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, scope, file);
                }

                return;
            case ModuleFunctionCallExpression moduleCall:
                AddFunctionReference(
                    $"{moduleCall.ModuleName}.{moduleCall.FunctionName}",
                    moduleCall.Line,
                    moduleCall.Column + 1,
                    moduleCall.Arguments.Count,
                    FindResolvedCall(moduleCall.Line, moduleCall.Column, file),
                    file);
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
            case ConditionalExpression conditional:
                VisitExpression(conditional.Condition, scope, file);
                VisitExpression(conditional.WhenTrue, scope, file);
                VisitExpression(conditional.WhenFalse, scope, file);
                return;
            case TemplateStringExpression template:
                foreach (var part in template.Expressions)
                {
                    VisitExpression(part, scope, file);
                }

                return;
            case ObjectLiteralExpression objectLiteral:
                foreach (var property in objectLiteral.Properties)
                {
                    VisitExpression(property.Value, scope, file);
                }

                return;
            case ParallelExpressionSyntax parallel:
                VisitTasks(parallel.Tasks, scope, file);
                return;
        }
    }

    private void VisitTasks(IEnumerable<TaskBlockSyntax> tasks, NavigationScope scope, string? file)
    {
        foreach (var task in tasks)
        {
            VisitStatements(task.Body, new NavigationScope(scope, isFunctionScope: false), file);
        }
    }

    private RuleScriptHoverInfo CreateHover(NavigationSymbol declaration, RuleScriptSourceRange? range, NavigationSymbol symbol)
    {
        switch (declaration.Kind)
        {
            case RuleScriptSymbolKind.Function:
            case RuleScriptSymbolKind.HostFunction:
                var function = symbol.ResolvedFunction
                    ?? FindFunctionSymbol(declaration, IsFunctionDeclaration(symbol) ? symbol.Parameters?.Count : symbol.ArgumentCount);
                var parameters = declaration.Parameters is { } declared && (function is null || declared.Count == function.Parameters.Count)
                    ? declared
                    : function?.Parameters ?? [];
                var returnType = function?.ReturnType ?? declaration.ReturnType;
                var prefix = function is null && declaration.Kind == RuleScriptSymbolKind.HostFunction
                    ? RuleScriptSignatureFormatter.GetFunctionPrefix(RuleScriptFunctionKind.Host)
                    : RuleScriptSignatureFormatter.GetFunctionPrefix(function?.Kind);
                var signature = $"{prefix} {RuleScriptSignatureFormatter.FormatSignature(declaration.Name, parameters, returnType).Label}";
                return new RuleScriptHoverInfo(
                    declaration.Name,
                    declaration.Kind,
                    signature,
                    function?.Documentation ?? declaration.Documentation,
                    range,
                    declaration.IsExternal);
            case RuleScriptSymbolKind.Parameter:
                return new RuleScriptHoverInfo(
                    declaration.Name,
                    declaration.Kind,
                    $"(parameter) {FormatTyped(declaration.Name, declaration.ValueType)}",
                    declaration.Documentation,
                    range,
                    declaration.IsExternal);
            default:
                var variable = _document.Analysis.Variables.FirstOrDefault(candidate => string.Equals(candidate.Name, declaration.Name, StringComparison.Ordinal));
                var keyword = variable?.IsReadOnly == true ? "const" : "var";
                return new RuleScriptHoverInfo(
                    declaration.Name,
                    declaration.Kind,
                    $"{keyword} {FormatTyped(declaration.Name, variable?.Type ?? RuleScriptValueType.Unknown)}",
                    declaration.Documentation ?? variable?.Documentation,
                    range,
                    declaration.IsExternal);
        }
    }

    private RuleScriptFunctionSymbol? FindFunctionSymbol(NavigationSymbol declaration, int? argumentCount)
    {
        var candidates = _document.Analysis.Functions
            .Where(function => string.Equals(function.Name, declaration.Name, StringComparison.Ordinal))
            .ToArray();

        if (declaration.Parameters is not null
            && candidates.FirstOrDefault(function => HasSignature(declaration, function.Parameters)) is { } exact)
        {
            return exact;
        }

        if (argumentCount is { } count)
        {
            var match = candidates.FirstOrDefault(function => function.Parameters.Count == count)
                ?? candidates.FirstOrDefault(function => function.HostMetadata?.IsVariadic == true);
            if (match is not null)
            {
                return match;
            }
        }

        return candidates.FirstOrDefault(function => declaration.Parameters is null || function.Parameters.Count == declaration.Parameters.Count)
            ?? candidates.FirstOrDefault();
    }

    private static string FormatTyped(string name, RuleScriptValueType type) => RuleScriptSignatureFormatter.FormatTyped(name, type);

    private static RuleScriptValueType ParseTypeName(string? typeName)
    {
        return typeName is not null && RuleScriptTypeFacts.TryParse(typeName, out var type)
            ? type
            : RuleScriptValueType.Unknown;
    }

    private NavigationSymbol? FindSymbol(int line, int column)
    {
        return _symbols.LastOrDefault(symbol => RuleScriptSourceMapper.Contains(symbol.SelectionRange, line, column));
    }

    /// <summary>
    /// The semantic analyzer only covers the open document, so calls inside imported files fall back to argument counts.
    /// </summary>
    private RuleScriptFunctionSymbol? FindResolvedCall(int? line, int? column, string? file)
    {
        return file is null
            && line is { } callLine
            && column is { } callColumn
            && _document.Analysis.ResolvedCalls.TryGetValue((callLine, callColumn), out var function)
                ? function
                : null;
    }

    private void AddFunctionReference(
        string name,
        int? line,
        int? column,
        int argumentCount,
        RuleScriptFunctionSymbol? resolvedFunction,
        string? file)
    {
        if (!TryCreateRange(file, line, column, name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name, out var range))
        {
            return;
        }

        var key = Key(RuleScriptSymbolKind.Function, name);
        if (_declarations.ContainsKey(key))
        {
            AddSymbol(new NavigationSymbol(name, RuleScriptSymbolKind.Function, range, range, IsDeclaration: false)
            {
                ArgumentCount = argumentCount,
                ResolvedFunction = resolvedFunction
            });
            return;
        }

        if (TryFindExternalFunction(name, out var externalFunction))
        {
            AddSymbol(externalFunction with
            {
                Range = range,
                SelectionRange = range,
                IsDeclaration = false,
                ArgumentCount = argumentCount,
                ResolvedFunction = resolvedFunction
            });
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
        var selectionRange = RuleScriptSourceMapper.CreateTokenRange(file, function.NameLine, function.NameColumn, function.Name);
        return new NavigationSymbol(
            name,
            RuleScriptSymbolKind.Function,
            RuleScriptSourceMapper.CreateRange(file, function.SourceSpan) ?? selectionRange,
            selectionRange,
            IsDeclaration: true,
            function.Documentation,
            Parameters: function.ParameterDefinitions.Select(parameter => new RuleScriptParameterSymbol(
                parameter.Name,
                ParseTypeName(parameter.TypeName))).ToArray());
    }

    private void AddDeclaration(NavigationSymbol symbol)
    {
        _declarations[symbol.Key] = symbol;
        if (symbol.Kind is RuleScriptSymbolKind.Function or RuleScriptSymbolKind.HostFunction && symbol.Parameters is not null)
        {
            if (!_functionOverloads.TryGetValue(symbol.Key, out var overloads))
            {
                overloads = [];
                _functionOverloads[symbol.Key] = overloads;
            }

            // Un-aliased imports register the same declaration twice; keep the first so references resolve to the visited symbol.
            if (symbol.SelectionRange is null || !overloads.Any(existing => Equals(existing.SelectionRange, symbol.SelectionRange)))
            {
                overloads.Add(symbol);
            }
        }

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

    private static bool TryCreateRange(string? file, int? line, int? column, string name, out RuleScriptSourceRange range)
    {
        range = null!;
        var tokenRange = RuleScriptSourceMapper.CreateTokenRange(file, line, column, name);
        if (tokenRange is null)
        {
            return false;
        }

        range = tokenRange;
        return true;
    }

    private bool TryFindExternalFunction(string name, out NavigationSymbol function)
    {
        var symbolKind = _document.Analysis.Functions
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
        public RuleScriptValueType ValueType { get; init; } = RuleScriptValueType.Unknown;

        public int? ArgumentCount { get; init; }

        public RuleScriptFunctionSymbol? ResolvedFunction { get; init; }

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
