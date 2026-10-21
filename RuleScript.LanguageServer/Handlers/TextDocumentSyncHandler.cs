using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace RuleScript.LanguageServer.Handlers;

/// <summary>
/// Keeps the workspace in sync with editor buffers and publishes diagnostics after every change.
/// </summary>
internal sealed class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;
    private readonly ILanguageServerFacade _server;

    public TextDocumentSyncHandler(RuleScriptWorkspace workspace, ILanguageServerFacade server)
    {
        _workspace = workspace;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, RuleScriptLanguageFeatures.LanguageId);
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspace.Update(request.TextDocument.Uri, request.TextDocument.Text, request.TextDocument.Version);
        PublishDiagnostics(request.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Full sync: the last change carries the whole document text.
        var change = request.ContentChanges.LastOrDefault();
        if (change is not null)
        {
            _workspace.Update(request.TextDocument.Uri, change.Text, request.TextDocument.Version);
            PublishDiagnostics(request.TextDocument.Uri);
        }

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspace.Close(request.TextDocument.Uri);
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = RuleScriptDocumentSelector.Instance,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false }
        };
    }

    private void PublishDiagnostics(DocumentUri uri)
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
}
