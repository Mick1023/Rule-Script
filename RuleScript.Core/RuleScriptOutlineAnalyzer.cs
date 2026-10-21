using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal static class RuleScriptOutlineAnalyzer
{
    public static IReadOnlyList<RuleScriptDocumentSymbol> GetDocumentSymbols(RuleScriptDocumentAnalysisResult document)
    {
        var symbols = new List<RuleScriptDocumentSymbol>();
        CollectSymbols(document.Statements, symbols);
        return symbols;
    }

    public static IReadOnlyList<RuleScriptFoldingRange> GetFoldingRanges(RuleScriptDocumentAnalysisResult document)
    {
        var ranges = new List<RuleScriptFoldingRange>();

        foreach (var region in document.Regions)
        {
            AddRange(ranges, region.StartLine, region.EndLine, RuleScriptFoldingRangeKind.Region);
        }

        CollectBlockRanges(document.Statements, ranges);
        CollectCommentRanges(document.Source, ranges);

        return ranges
            .GroupBy(range => range.StartLine)
            .Select(group => group.MaxBy(range => range.EndLine)!)
            .OrderBy(range => range.StartLine)
            .ToArray();
    }

    private static void CollectSymbols(IEnumerable<Statement> statements, List<RuleScriptDocumentSymbol> symbols)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclarationStatement function:
                    var children = new List<RuleScriptDocumentSymbol>();
                    CollectSymbols(function.Body, children);
                    AddSymbol(
                        symbols,
                        function.Name,
                        RuleScriptDocumentSymbolKind.Function,
                        FormatFunctionDetail(function),
                        statement,
                        function.NameLine,
                        function.NameColumn,
                        children);
                    break;
                case VarStatement variable:
                    AddSymbol(symbols, variable.Name, RuleScriptDocumentSymbolKind.Variable, null, statement, variable.Line, variable.Column, []);
                    break;
                case ConstStatement constant:
                    AddSymbol(symbols, constant.Name, RuleScriptDocumentSymbolKind.Constant, null, statement, constant.Line, constant.Column, []);
                    break;
                case ImportStatement import:
                    var range = statement.Range;
                    if (range is not null)
                    {
                        symbols.Add(new RuleScriptDocumentSymbol(
                            import.Alias ?? import.Path,
                            RuleScriptDocumentSymbolKind.Import,
                            import.Path,
                            range,
                            range,
                            []));
                    }

                    break;
                default:
                    foreach (var body in GetChildBodies(statement))
                    {
                        CollectSymbols(body, symbols);
                    }

                    break;
            }
        }
    }

    private static void AddSymbol(
        List<RuleScriptDocumentSymbol> symbols,
        string name,
        RuleScriptDocumentSymbolKind kind,
        string? detail,
        Statement statement,
        int? nameLine,
        int? nameColumn,
        IReadOnlyList<RuleScriptDocumentSymbol> children)
    {
        var selectionRange = RuleScriptSourceMapper.CreateTokenRange(null, nameLine, nameColumn, name);
        var range = statement.Range ?? selectionRange;

        if (range is null || selectionRange is null)
        {
            return;
        }

        symbols.Add(new RuleScriptDocumentSymbol(name, kind, detail, range, selectionRange, children));
    }

    private static string FormatFunctionDetail(FunctionDeclarationStatement function)
    {
        var parameters = string.Join(", ", function.ParameterDefinitions.Select(parameter =>
            parameter.TypeName is null ? parameter.Name : $"{parameter.Name}: {parameter.TypeName}"));
        var detail = $"({parameters})";

        if (function.ReturnTypeName is not null)
        {
            detail += $" -> {function.ReturnTypeName}";
        }

        return function.HostTriggerName is null
            ? detail
            : $"@HostTrigger(\"{function.HostTriggerName}\") {detail}";
    }

    private static void CollectBlockRanges(IEnumerable<Statement> statements, List<RuleScriptFoldingRange> ranges)
    {
        foreach (var statement in statements)
        {
            if (statement.SourceSpan is { } span)
            {
                AddRange(ranges, span.StartLine, span.EndLine, RuleScriptFoldingRangeKind.Block);
            }

            foreach (var body in GetChildBodies(statement))
            {
                CollectBlockRanges(body, ranges);
            }
        }
    }

    private static void CollectCommentRanges(string source, List<RuleScriptFoldingRange> ranges)
    {
        var tokens = new Lexer.Lexer(source).TokenizeWithDiagnostics([], includeComments: true);
        Token? documentationStart = null;
        Token? documentationEnd = null;

        foreach (var token in tokens)
        {
            if (token.Type == TokenType.DocumentationComment
                && documentationEnd is not null
                && token.Line == documentationEnd.Line + 1)
            {
                documentationEnd = token;
                continue;
            }

            if (documentationStart is not null)
            {
                AddRange(ranges, documentationStart.Line, documentationEnd!.Line, RuleScriptFoldingRangeKind.Comment);
                documentationStart = null;
                documentationEnd = null;
            }

            if (token.Type == TokenType.DocumentationComment)
            {
                documentationStart = token;
                documentationEnd = token;
            }
            else if (token.Type == TokenType.MultiLineComment)
            {
                AddRange(ranges, token.Line, token.EndLine, RuleScriptFoldingRangeKind.Comment);
            }
        }
    }

    private static IEnumerable<IReadOnlyList<Statement>> GetChildBodies(Statement statement)
    {
        switch (statement)
        {
            case FunctionDeclarationStatement function:
                yield return function.Body;
                break;
            case IfStatement ifStatement:
                yield return ifStatement.ThenBranch;
                yield return ifStatement.ElseBranch;
                break;
            case WhileStatement whileStatement:
                yield return whileStatement.Body;
                break;
            case ForeachStatement foreachStatement:
                yield return foreachStatement.Body;
                break;
            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    yield return switchCase.Body;
                }

                if (switchStatement.DefaultBranch is not null)
                {
                    yield return switchStatement.DefaultBranch;
                }

                break;
            case ParallelStatementSyntax parallel:
                foreach (var task in parallel.Tasks)
                {
                    yield return task.Body;
                }

                break;
        }
    }

    private static void AddRange(List<RuleScriptFoldingRange> ranges, int startLine, int endLine, RuleScriptFoldingRangeKind kind)
    {
        if (endLine > startLine)
        {
            ranges.Add(new RuleScriptFoldingRange(startLine, endLine, kind));
        }
    }
}
