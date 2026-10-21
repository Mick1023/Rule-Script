using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RuleScript.LanguageServer.Handlers;

internal sealed class CompletionHandler : CompletionHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;
    private bool _snippetSupport;

    public CompletionHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult(document is null
            ? new CompletionList()
            : RuleScriptLanguageFeatures.GetCompletions(document, request.Position, _snippetSupport));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        // Items are fully populated up front, so resolve has nothing to add.
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        // OmniSharp passes null at runtime when the client does not declare completion capabilities.
        _snippetSupport = capability?.CompletionItem?.SnippetSupport == true;

        return new CompletionRegistrationOptions
        {
            DocumentSelector = RuleScriptDocumentSelector.Instance,
            TriggerCharacters = new Container<string>(".", "@"),
            ResolveProvider = false
        };
    }
}

internal sealed class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public SignatureHelpHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult(document is null ? null : RuleScriptLanguageFeatures.GetSignatureHelp(document, request.Position));
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = RuleScriptDocumentSelector.Instance,
            TriggerCharacters = new Container<string>("(", ","),
            RetriggerCharacters = new Container<string>(",")
        };
    }
}
