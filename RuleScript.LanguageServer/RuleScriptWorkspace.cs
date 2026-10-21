using System.Collections.Concurrent;
using System.Text.Json;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RuleScript.Core;
using RuleScript.Core.Runtime;

namespace RuleScript.LanguageServer;

/// <summary>
/// Tracks open editor buffers and workspace folders, and lazily analyzes each document version once.
/// Each workspace folder has its own engine configured from that folder's <c>rulescript.json</c>.
/// </summary>
internal sealed class RuleScriptWorkspace
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents = new();
    private IReadOnlyList<WorkspaceFolder> _folders = [];
    private RuleScriptEngine _defaultEngine;

    public RuleScriptWorkspace()
    {
        _defaultEngine = CreateEngine(root: null);
    }

    /// <summary>
    /// Receives warnings about invalid workspace configuration, such as a malformed <c>rulescript.json</c>.
    /// </summary>
    public Action<string>? Warn { get; set; }

    public void Initialize(IEnumerable<DocumentUri> roots)
    {
        _folders = roots
            .Where(IsFile)
            .Select(root => Path.GetFullPath(root.GetFileSystemPath()))
            .Distinct(StringComparer.FromComparison(PathComparison))
            .Select(path => new WorkspaceFolder(path, CreateEngine(path)))
            // Longest paths first, so nested folders win over their parents.
            .OrderByDescending(folder => folder.Path.Length)
            .ToArray();
        _defaultEngine = CreateEngine(root: null);
        _documents.Clear();
    }

    /// <summary>
    /// Rebuilds the engine for the folder that owns a changed <c>rulescript.json</c>.
    /// Returns whether open documents need to be re-analyzed.
    /// </summary>
    public bool ReloadManifest(string manifestPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        var folders = _folders.ToArray();
        var index = Array.FindIndex(folders, folder => string.Equals(folder.Path, directory, PathComparison));

        if (index < 0)
        {
            return false;
        }

        folders[index] = new WorkspaceFolder(folders[index].Path, CreateEngine(folders[index].Path));
        _folders = folders;
        InvalidateAll();
        return true;
    }

    public void Update(DocumentUri uri, string text, int? version)
    {
        _documents[uri] = CreateState(uri, text, version);

        // Other documents may import this one, so their cached analyses are stale now.
        foreach (var other in _documents.Keys.Where(other => other != uri))
        {
            Invalidate(other);
        }
    }

    public bool Close(DocumentUri uri)
    {
        var removed = _documents.TryRemove(uri, out _);
        if (removed)
        {
            InvalidateAll();
        }

        return removed;
    }

    public IReadOnlyList<DocumentUri> OpenDocuments => _documents.Keys.ToArray();

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

    public RuleScriptEngine GetEngine(DocumentUri uri)
    {
        var path = GetFilePath(uri);
        if (path is null)
        {
            return _defaultEngine;
        }

        var fullPath = Path.GetFullPath(path);
        return _folders.FirstOrDefault(folder => IsInside(fullPath, folder.Path))?.Engine ?? _defaultEngine;
    }

    public static string? GetFilePath(DocumentUri uri)
    {
        return IsFile(uri) ? uri.GetFileSystemPath() : null;
    }

    private DocumentState CreateState(DocumentUri uri, string text, int? version)
    {
        return new DocumentState(text, version, new Lazy<RuleScriptDocumentAnalysisResult>(() => Analyze(uri, text)));
    }

    private void Invalidate(DocumentUri uri)
    {
        if (_documents.TryGetValue(uri, out var state))
        {
            _documents.TryUpdate(uri, CreateState(uri, state.Text, state.Version), state);
        }
    }

    private void InvalidateAll()
    {
        foreach (var uri in _documents.Keys)
        {
            Invalidate(uri);
        }
    }

    private RuleScriptDocumentAnalysisResult Analyze(DocumentUri uri, string text)
    {
        var engine = GetEngine(uri);
        var path = GetFilePath(uri);
        return path is null
            ? RuleScriptLanguageService.TryAnalyzeDocument(engine, text)
            : RuleScriptLanguageService.TryAnalyzeDocument(engine, text, path);
    }

    private RuleScriptEngine CreateEngine(string? root)
    {
        var engine = root is null ? new RuleScriptEngine() : new RuleScriptEngine { WorkingDirectory = root };
        engine.ImportResolver = new OpenDocumentImportResolver(GetOpenText, engine.ImportResolver);

        var manifestPath = root is null ? null : Path.Combine(root, RuleScriptManifest.FileName);
        if (manifestPath is not null && File.Exists(manifestPath))
        {
            try
            {
                foreach (var problem in RuleScriptManifest.Parse(File.ReadAllText(manifestPath)).ApplyTo(engine))
                {
                    Warn?.Invoke($"{manifestPath}: {problem}");
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                Warn?.Invoke($"{manifestPath} could not be read: {exception.Message}");
            }
        }

        return engine;
    }

    private string? GetOpenText(string fullPath)
    {
        foreach (var (uri, state) in _documents)
        {
            var path = GetFilePath(uri);
            if (path is not null && string.Equals(Path.GetFullPath(path), fullPath, PathComparison))
            {
                return state.Text;
            }
        }

        return null;
    }

    private static bool IsInside(string path, string folder)
    {
        return path.StartsWith(folder, PathComparison)
            && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar || folder.EndsWith(Path.DirectorySeparatorChar));
    }

    private static bool IsFile(DocumentUri uri)
    {
        return string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record WorkspaceFolder(string Path, RuleScriptEngine Engine);

    private sealed record DocumentState(
        string Text,
        int? Version,
        Lazy<RuleScriptDocumentAnalysisResult> Analysis);
}
