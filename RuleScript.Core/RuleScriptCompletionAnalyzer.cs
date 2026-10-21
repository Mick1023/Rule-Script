using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal static class RuleScriptCompletionAnalyzer
{
    private static readonly string[] Keywords =
    [
        "var", "const", "export", "if", "then", "else", "elseif", "endif", "while", "endwhile",
        "foreach", "in", "endforeach", "function", "endfunction", "return", "import", "as",
        "break", "continue", "switch", "case", "default", "when", "endswitch",
        "parallel", "task", "trigger", "endtask", "endparallel",
        "and", "or", "true", "false", "null", "global"
    ];

    private static readonly (string Label, string Detail, string Body)[] Snippets =
    [
        ("if", "if ... then: ... endif", "if ${1:condition} then:\n\t$0\nendif"),
        ("ifelse", "if ... then: ... else: ... endif", "if ${1:condition} then:\n\t$2\nelse:\n\t$0\nendif"),
        ("foreach", "foreach ... in ...: ... endforeach", "foreach ${1:item} in ${2:items}:\n\t$0\nendforeach"),
        ("while", "while ...: ... endwhile", "while ${1:condition}:\n\t$0\nendwhile"),
        ("function", "function ...(): ... endfunction", "function ${1:Name}(${2}):\n\t$0\nendfunction"),
        ("switch", "switch ...: case ...: ... endswitch", "switch ${1:value}:\n\tcase ${2:value}:\n\t\t$0\nendswitch")
    ];

    private static readonly HashSet<TokenType> DeclarationKeywords =
    [
        TokenType.Var,
        TokenType.Const,
        TokenType.Function,
        TokenType.Foreach,
        TokenType.As,
        TokenType.Import
    ];

    public static RuleScriptCompletionList GetCompletions(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var source = document.Source;
        var cursor = ToOffset(source, line, column);
        var wordStart = cursor;
        while (wordStart > 0 && IsIdentifierPart(source[wordStart - 1]))
        {
            wordStart--;
        }

        var replaceRange = new RuleScriptSourceRange(null, line, column - (cursor - wordStart), line, column);
        var empty = new RuleScriptCompletionList([], replaceRange);

        if (IsInsideStringOrComment(source, line, column))
        {
            return empty;
        }

        var before = wordStart > 0 ? source[wordStart - 1] : '\0';
        if (before == '.')
        {
            return new RuleScriptCompletionList(GetMemberCompletions(document, source, wordStart - 1), replaceRange);
        }

        if (before == '@')
        {
            return new RuleScriptCompletionList(
                [new RuleScriptCompletionItem("HostTrigger", RuleScriptCompletionItemKind.Attribute, "@HostTrigger(\"name\")", null, "HostTrigger(\"${1:name}\")", IsSnippet: true)],
                replaceRange);
        }

        var wordStartColumn = column - (cursor - wordStart);
        var previous = document.Tokens.LastOrDefault(token =>
            token.Type != TokenType.EndOfFile && IsBefore(token.EndLine, token.EndColumn, line, wordStartColumn, inclusive: true));
        if (previous is not null && DeclarationKeywords.Contains(previous.Type))
        {
            // The user is typing a new name, so existing symbols would only get in the way.
            return empty;
        }

        return new RuleScriptCompletionList(GetGeneralCompletions(document, line, column), replaceRange);
    }

    private static IReadOnlyList<RuleScriptCompletionItem> GetMemberCompletions(
        RuleScriptDocumentAnalysisResult document,
        string source,
        int dotOffset)
    {
        var targetEnd = dotOffset;
        if (targetEnd > 0 && source[targetEnd - 1] == '?')
        {
            targetEnd--;
        }

        var targetStart = targetEnd;
        while (targetStart > 0 && IsIdentifierPart(source[targetStart - 1]))
        {
            targetStart--;
        }

        var target = source[targetStart..targetEnd];

        if (target == "global")
        {
            return document.Analysis.Variables
                .Select(variable => CreateVariableItem(variable))
                .ToArray();
        }

        if (!document.Analysis.ImportAliases.Contains(target, StringComparer.Ordinal))
        {
            // Members of objects and JSON values are only known at runtime.
            return [];
        }

        var prefix = target + ".";
        return document.Analysis.Functions
            .Where(function => function.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(function => CreateFunctionItem(function, function.Name[prefix.Length..]))
            .DistinctBy(item => item.Label)
            .ToArray();
    }

    private static IReadOnlyList<RuleScriptCompletionItem> GetGeneralCompletions(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var items = new List<RuleScriptCompletionItem>();
        var function = FindEnclosingFunction(document.Statements, line, column);

        if (function is not null)
        {
            items.AddRange(function.ParameterDefinitions.Select(parameter => new RuleScriptCompletionItem(
                parameter.Name,
                RuleScriptCompletionItemKind.Parameter,
                parameter.TypeName is null ? "parameter" : $"parameter: {parameter.TypeName}",
                null,
                parameter.Name)));
        }

        var baseDirectory = document.BaseDirectory ?? document.Engine.ResolveDocumentBaseDirectory(null);
        var visible = document.Engine.AnalyzeAtCursor(document.Source, baseDirectory, line, column);
        items.AddRange(visible.VisibleVariables.Select(variable => CreateVariableItem(variable)));

        items.AddRange(document.Analysis.Functions
            .Where(candidate => !candidate.Name.Contains('.'))
            .Select(candidate => CreateFunctionItem(candidate, candidate.Name)));

        items.AddRange(document.Analysis.ImportAliases.Select(alias => new RuleScriptCompletionItem(
            alias,
            RuleScriptCompletionItemKind.Module,
            "import alias",
            null,
            alias)));

        items.AddRange(Keywords.Select(keyword => new RuleScriptCompletionItem(
            keyword,
            RuleScriptCompletionItemKind.Keyword,
            null,
            null,
            keyword)));

        items.AddRange(Snippets.Select(snippet => new RuleScriptCompletionItem(
            snippet.Label,
            RuleScriptCompletionItemKind.Snippet,
            snippet.Detail,
            null,
            snippet.Body,
            IsSnippet: true)));

        // Parameters shadow variables with the same name; symbols win over keywords with the same text.
        return items
            .DistinctBy(item => item.Kind == RuleScriptCompletionItemKind.Snippet ? $"snippet:{item.Label}" : item.Label)
            .ToArray();
    }

    private static RuleScriptCompletionItem CreateVariableItem(RuleScriptVariableSymbol variable)
    {
        var detail = RuleScriptSignatureFormatter.IsKnown(variable.Type)
            ? RuleScriptTypeFacts.ToDisplayName(variable.Type)
            : null;

        return new RuleScriptCompletionItem(
            variable.Name,
            variable.IsReadOnly ? RuleScriptCompletionItemKind.Constant : RuleScriptCompletionItemKind.Variable,
            detail,
            variable.Documentation,
            variable.Name);
    }

    private static RuleScriptCompletionItem CreateFunctionItem(RuleScriptFunctionSymbol function, string label)
    {
        var kind = function.Kind switch
        {
            RuleScriptFunctionKind.Host => RuleScriptCompletionItemKind.HostFunction,
            RuleScriptFunctionKind.Builtin => RuleScriptCompletionItemKind.BuiltinFunction,
            _ => RuleScriptCompletionItemKind.Function
        };
        var signature = RuleScriptSignatureFormatter.FormatSignature(label, function.Parameters, function.ReturnType).Label;

        return new RuleScriptCompletionItem(
            label,
            kind,
            $"{RuleScriptSignatureFormatter.GetFunctionPrefix(function.Kind)} {signature}",
            function.Documentation,
            label);
    }

    private static FunctionDeclarationStatement? FindEnclosingFunction(IEnumerable<Statement> statements, int line, int column)
    {
        return statements
            .OfType<FunctionDeclarationStatement>()
            .FirstOrDefault(function => function.SourceSpan is { } span
                && IsBefore(span.StartLine, span.StartColumn, line, column, inclusive: true)
                && IsBefore(line, column, span.EndLine, span.EndColumn, inclusive: true));
    }

    private static bool IsInsideStringOrComment(string source, int line, int column)
    {
        var tokens = new Lexer.Lexer(source).TokenizeWithDiagnostics([], includeComments: true);

        foreach (var token in tokens)
        {
            if (!IsBefore(token.Line, token.Column, line, column, inclusive: false))
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.String:
                    var terminated = token.Lexeme.Length > 1 && token.Lexeme[^1] == '"';
                    if (IsBefore(line, column, token.EndLine, token.EndColumn, inclusive: !terminated))
                    {
                        return true;
                    }

                    break;
                case TokenType.LineComment:
                case TokenType.MultiLineComment:
                case TokenType.DocumentationComment:
                case TokenType.RegionStart:
                case TokenType.RegionEnd:
                    if (IsBefore(line, column, token.EndLine, token.EndColumn, inclusive: true))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    internal static int ToOffset(string source, int line, int column)
    {
        var offset = 0;
        for (var current = 1; current < line && offset < source.Length; current++)
        {
            var next = source.IndexOf('\n', offset);
            if (next < 0)
            {
                return source.Length;
            }

            offset = next + 1;
        }

        var lineEnd = source.IndexOf('\n', offset);
        var lineLength = (lineEnd < 0 ? source.Length : lineEnd) - offset;
        return offset + Math.Clamp(column - 1, 0, lineLength);
    }

    /// <summary>
    /// Compares two 1-based positions; with <paramref name="inclusive"/> the positions may be equal.
    /// </summary>
    internal static bool IsBefore(int line, int column, int otherLine, int otherColumn, bool inclusive)
    {
        return line < otherLine || (line == otherLine && (inclusive ? column <= otherColumn : column < otherColumn));
    }

    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';
}
