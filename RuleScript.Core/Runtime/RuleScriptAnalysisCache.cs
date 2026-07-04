namespace RuleScript.Core.Runtime;

/// <summary>
/// Reuses per-document analysis results for editor integrations.
/// </summary>
public sealed class RuleScriptAnalysisCache
{
    private readonly RuleScriptEngine _engine;
    private readonly Dictionary<string, CachedDocument> _documents = new(StringComparer.Ordinal);

    public RuleScriptAnalysisCache()
        : this(new RuleScriptEngine())
    {
    }

    public RuleScriptAnalysisCache(RuleScriptEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Gets an existing analysis result for unchanged source text or analyzes and stores a fresh result.
    /// </summary>
    public RuleScriptDocumentAnalysisResult GetOrAnalyze(string documentId, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(source);

        if (_documents.TryGetValue(documentId, out var cached)
            && string.Equals(cached.Source, source, StringComparison.Ordinal))
        {
            return cached.Result;
        }

        var result = RuleScriptLanguageService.AnalyzeDocument(_engine, source);
        _documents[documentId] = new CachedDocument(source, result);
        return result;
    }

    /// <summary>
    /// Attempts to get the latest cached analysis result for a document.
    /// </summary>
    public bool TryGetDocument(string documentId, out RuleScriptDocumentAnalysisResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        if (_documents.TryGetValue(documentId, out var cached))
        {
            result = cached.Result;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Removes a document from the cache.
    /// </summary>
    public bool Remove(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return _documents.Remove(documentId);
    }

    /// <summary>
    /// Removes all cached document analyses.
    /// </summary>
    public void Clear()
    {
        _documents.Clear();
    }

    private sealed record CachedDocument(string Source, RuleScriptDocumentAnalysisResult Result);
}
