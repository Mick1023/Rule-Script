using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace RuleScript.LanguageServer.Handlers;

/// <summary>
/// Keeps the workspace in sync with editor buffers and publishes diagnostics after every change.
/// </summary>
internal sealed class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;
    private readonly DiagnosticsPublisher _diagnostics;

    public TextDocumentSyncHandler(RuleScriptWorkspace workspace, DiagnosticsPublisher diagnostics)
    {
        _workspace = workspace;
        _diagnostics = diagnostics;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, RuleScriptLanguageFeatures.LanguageId);
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspace.Update(request.TextDocument.Uri, request.TextDocument.Text, request.TextDocument.Version);
        _diagnostics.Publish(request.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Full sync: the last change carries the whole document text.
        var change = request.ContentChanges.LastOrDefault();
        if (change is not null)
        {
            _workspace.Update(request.TextDocument.Uri, change.Text, request.TextDocument.Version);
            _diagnostics.Publish(request.TextDocument.Uri);
        }

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Saving is a natural point to refresh documents that import the saved file.
        _diagnostics.PublishAll();
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspace.Close(request.TextDocument.Uri);
        _diagnostics.Clear(request.TextDocument.Uri);
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
}
