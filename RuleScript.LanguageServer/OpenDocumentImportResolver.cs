using RuleScript.Core.Runtime;

namespace RuleScript.LanguageServer;

/// <summary>
/// Resolves imports from open editor buffers first, so unsaved edits in imported files are visible to importers.
/// </summary>
internal sealed class OpenDocumentImportResolver : IImportResolver
{
    private readonly Func<string, string?> _getOpenText;
    private readonly IImportResolver _inner;

    public OpenDocumentImportResolver(Func<string, string?> getOpenText, IImportResolver inner)
    {
        _getOpenText = getOpenText;
        _inner = inner;
    }

    public string GetFullPath(string path) => _inner.GetFullPath(path);

    public bool Exists(string path) => _getOpenText(GetFullPath(path)) is not null || _inner.Exists(path);

    public string ReadAllText(string path) => _getOpenText(GetFullPath(path)) ?? _inner.ReadAllText(path);
}
