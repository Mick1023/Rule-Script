using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace RuleScript.LanguageServer;

/// <summary>
/// Publishes diagnostics for one document, or for every open document after a change that can affect importers.
/// </summary>
internal sealed class DiagnosticsPublisher
{
    private readonly RuleScriptWorkspace _workspace;
    private readonly ILanguageServerFacade _server;

    public DiagnosticsPublisher(RuleScriptWorkspace workspace, ILanguageServerFacade server)
    {
        _workspace = workspace;
        _server = server;
    }

    public void Publish(DocumentUri uri)
    {
        var document = _workspace.GetDocument(uri);
        if (document is null)
        {
            return;
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Version = _workspace.GetVersion(uri),
            Diagnostics = new Container<Diagnostic>(
                RuleScriptLanguageFeatures.GetDiagnostics(document, RuleScriptWorkspace.GetFilePath(uri)))
        });
    }

    public void PublishAll()
    {
        foreach (var uri in _workspace.OpenDocuments)
        {
            Publish(uri);
        }
    }

    public void Clear(DocumentUri uri)
    {
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>()
        });
    }
}
