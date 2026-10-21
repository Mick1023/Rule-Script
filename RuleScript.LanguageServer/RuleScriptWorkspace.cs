using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.LanguageServer;

/// <summary>
/// Tracks open editor buffers and lazily analyzes each document version once.
/// </summary>
internal sealed class RuleScriptWorkspace
{
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents = new();

    public RuleScriptEngine Engine { get; private set; } = new();

    public void Initialize(DocumentUri? rootUri)
    {
        Engine = rootUri is not null && IsFile(rootUri)
            ? new RuleScriptEngine { WorkingDirectory = rootUri.GetFileSystemPath() }
            : new RuleScriptEngine();
        _documents.Clear();
    }

    public void Update(DocumentUri uri, string text, int? version)
    {
        var engine = Engine;
        _documents[uri] = new DocumentState(
            text,
            version,
            new Lazy<RuleScriptDocumentAnalysisResult>(() => Analyze(engine, uri, text)));
    }

    public bool Close(DocumentUri uri)
    {
        return _documents.TryRemove(uri, out _);
    }

    public string? GetText(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state.Text : null;
    }

    public int? GetVersion(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state.Version : null;
    }

    public RuleScriptDocumentAnalysisResult? GetDocument(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state.Analysis.Value : null;
    }

    public static string? GetFilePath(DocumentUri uri)
    {
        return IsFile(uri) ? uri.GetFileSystemPath() : null;
    }

    private static RuleScriptDocumentAnalysisResult Analyze(RuleScriptEngine engine, DocumentUri uri, string text)
    {
        var path = GetFilePath(uri);
        return path is null
            ? RuleScriptLanguageService.TryAnalyzeDocument(engine, text)
            : RuleScriptLanguageService.TryAnalyzeDocument(engine, text, path);
    }

    private static bool IsFile(DocumentUri uri)
    {
        return string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DocumentState(
        string Text,
        int? Version,
        Lazy<RuleScriptDocumentAnalysisResult> Analysis);
}
