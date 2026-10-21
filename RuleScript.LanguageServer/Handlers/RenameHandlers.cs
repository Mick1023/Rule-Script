using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RuleScript.LanguageServer.Handlers;

internal static class RenameErrors
{
    // LSP 3.17 RequestFailed: the request was valid but could not be completed; clients show the message.
    private const int RequestFailed = -32803;

    public static T Run<T>(Func<T> rename)
    {
        try
        {
            return rename();
        }
        catch (RenameRejectedException exception)
        {
            throw new RpcErrorException(RequestFailed, null!, exception.Message);
        }
    }
}

internal sealed class RenameHandler : RenameHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public RenameHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult(document is null
            ? null
            : RenameErrors.Run(() => RuleScriptLanguageFeatures.Rename(document, request.TextDocument.Uri, request.Position, request.NewName)));
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = RuleScriptDocumentSelector.Instance,
            PrepareProvider = true
        };
    }
}

internal sealed class PrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public PrepareRenameHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult<RangeOrPlaceholderRange?>(document is null
            ? null
            : RenameErrors.Run(() => RuleScriptLanguageFeatures.PrepareRename(document, request.Position)));
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = RuleScriptDocumentSelector.Instance,
            PrepareProvider = true
        };
    }
}
