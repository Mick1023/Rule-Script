using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RuleScript.Core;
using RuleScript.Core.Diagnostics;
using RuleScript.Core.Formatting;
using RuleScript.Core.Runtime;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RuleScript.LanguageServer;

/// <summary>
/// Maps RuleScript language service results to LSP protocol models, independent of the JSON-RPC transport.
/// </summary>
internal static partial class RuleScriptLanguageFeatures
{
    public const string LanguageId = "rulescript";

    public const string DiagnosticSource = "rulescript";

    public static IReadOnlyList<Diagnostic> GetDiagnostics(RuleScriptDocumentAnalysisResult document, string? filePath)
    {
        return document.Diagnostics
            .Where(diagnostic => BelongsToDocument(diagnostic, filePath))
            .Select(ToLspDiagnostic)
            .ToArray();
    }

    public static IReadOnlyList<Location> GetDefinition(RuleScriptDocumentAnalysisResult document, DocumentUri uri, Position position)
    {
        var (line, column) = PositionMapper.ToRuleScript(position);
        var definition = RuleScriptLanguageService.GetDefinition(document, line, column);
        var range = definition?.SelectionRange ?? definition?.Range;

        return definition is null || definition.IsExternal || range is null
            ? []
            : [new Location { Uri = ResolveUri(range.File, uri), Range = PositionMapper.ToLsp(range) }];
    }

    public static IReadOnlyList<Location> GetReferences(
        RuleScriptDocumentAnalysisResult document,
        DocumentUri uri,
        Position position,
        bool includeDeclaration)
    {
        var (line, column) = PositionMapper.ToRuleScript(position);

        return RuleScriptLanguageService.FindReferences(document, line, column)
            .Where(reference => reference.Range is not null && (includeDeclaration || !reference.IsDeclaration))
            .Select(reference => new Location
            {
                Uri = ResolveUri(reference.Range!.File, uri),
                Range = PositionMapper.ToLsp(reference.Range)
            })
            .ToArray();
    }

    public static Hover? GetHover(RuleScriptDocumentAnalysisResult document, Position position)
    {
        var (line, column) = PositionMapper.ToRuleScript(position);
        var hover = RuleScriptLanguageService.GetHover(document, line, column);

        if (hover is null)
        {
            return null;
        }

        var markdown = $"```{LanguageId}\n{hover.Signature}\n```";
        if (!string.IsNullOrWhiteSpace(hover.Documentation))
        {
            markdown += $"\n\n{hover.Documentation}";
        }

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = hover.Range is null ? null : PositionMapper.ToLsp(hover.Range)
        };
    }

    public static IReadOnlyList<DocumentSymbol> GetDocumentSymbols(RuleScriptDocumentAnalysisResult document)
    {
        return RuleScriptLanguageService.GetDocumentSymbols(document).Select(ToLspSymbol).ToArray();
    }

    public static IReadOnlyList<FoldingRange> GetFoldingRanges(RuleScriptDocumentAnalysisResult document)
    {
        return RuleScriptLanguageService.GetFoldingRanges(document)
            .Select(range => new
            {
                range.Kind,
                StartLine = range.StartLine - 1,
                // Keep the closing keyword (endif, endfunction, ...) visible when a block is folded.
                EndLine = range.Kind == RuleScriptFoldingRangeKind.Block ? range.EndLine - 2 : range.EndLine - 1
            })
            .Where(range => range.EndLine > range.StartLine)
            .Select(range => range.Kind switch
            {
                RuleScriptFoldingRangeKind.Region => new FoldingRange { StartLine = range.StartLine, EndLine = range.EndLine, Kind = FoldingRangeKind.Region },
                RuleScriptFoldingRangeKind.Comment => new FoldingRange { StartLine = range.StartLine, EndLine = range.EndLine, Kind = FoldingRangeKind.Comment },
                _ => new FoldingRange { StartLine = range.StartLine, EndLine = range.EndLine }
            })
            .ToArray();
    }

    public static IReadOnlyList<TextEdit> Format(string text)
    {
        string formatted;
        try
        {
            formatted = RuleScriptFormatter.Format(text);
        }
        catch (SyntaxException)
        {
            // Formatting a document with syntax errors could reshape code the user is still typing.
            return [];
        }

        return string.Equals(formatted, text, StringComparison.Ordinal)
            ? []
            : [new TextEdit { Range = GetFullRange(text), NewText = formatted }];
    }

    private static Diagnostic ToLspDiagnostic(RuleScriptDiagnostic diagnostic)
    {
        return new Diagnostic
        {
            Range = GetDiagnosticRange(diagnostic),
            Severity = diagnostic.Severity switch
            {
                RuleScriptDiagnosticSeverity.Error => DiagnosticSeverity.Error,
                RuleScriptDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Information
            },
            Code = diagnostic.Code,
            Source = DiagnosticSource,
            Message = StripLocationPrefix(diagnostic.Message)
        };
    }

    /// <summary>
    /// Removes the "Line 1, Column 9: Syntax error: " prefix that core messages carry, because editors already show the range.
    /// </summary>
    private static string StripLocationPrefix(string message)
    {
        return LocationPrefix().Replace(message, string.Empty, 1);
    }

    [GeneratedRegex(@"^(File '[^']*'(, Line \d+, Column \d+)?|Line \d+, Column \d+): (Syntax error: )?")]
    private static partial Regex LocationPrefix();

    private static LspRange GetDiagnosticRange(RuleScriptDiagnostic diagnostic)
    {
        if (diagnostic.Range is { } range)
        {
            return PositionMapper.ToLsp(range);
        }

        var start = PositionMapper.ToLsp(diagnostic.Line ?? 1, diagnostic.Column ?? 1);
        var length = Math.Max(1, diagnostic.TokenText?.Length ?? 1);
        return new LspRange(start, new Position(start.Line, start.Character + length));
    }

    private static bool BelongsToDocument(RuleScriptDiagnostic diagnostic, string? filePath)
    {
        var file = diagnostic.SourceFile ?? diagnostic.Range?.File;
        return file is null
            || (filePath is not null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));
    }

    private static DocumentSymbol ToLspSymbol(RuleScriptDocumentSymbol symbol)
    {
        return new DocumentSymbol
        {
            Name = symbol.Name,
            Detail = symbol.Detail,
            Kind = symbol.Kind switch
            {
                RuleScriptDocumentSymbolKind.Function => SymbolKind.Function,
                RuleScriptDocumentSymbolKind.Constant => SymbolKind.Constant,
                RuleScriptDocumentSymbolKind.Import => SymbolKind.Module,
                _ => SymbolKind.Variable
            },
            Range = PositionMapper.ToLsp(symbol.Range),
            SelectionRange = PositionMapper.ToLsp(symbol.SelectionRange),
            Children = symbol.Children.Count == 0 ? null : new Container<DocumentSymbol>(symbol.Children.Select(ToLspSymbol))
        };
    }

    private static DocumentUri ResolveUri(string? file, DocumentUri currentDocument)
    {
        return file is null ? currentDocument : DocumentUri.FromFileSystemPath(file);
    }

    private static LspRange GetFullRange(string text)
    {
        var lines = text.Split('\n');
        return new LspRange(new Position(0, 0), new Position(lines.Length - 1, lines[^1].Length));
    }
}
