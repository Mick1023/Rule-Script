using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RuleScript.LanguageServer.Handlers;

internal sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public DefinitionHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        if (document is null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var locations = RuleScriptLanguageFeatures.GetDefinition(document, request.TextDocument.Uri, request.Position);
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(locations.Select(location => new LocationOrLocationLink(location))));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}

internal sealed class ReferencesHandler : ReferencesHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public ReferencesHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        if (document is null)
        {
            return Task.FromResult<LocationContainer?>(null);
        }

        var locations = RuleScriptLanguageFeatures.GetReferences(
            document,
            request.TextDocument.Uri,
            request.Position,
            request.Context?.IncludeDeclaration ?? true);
        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}

internal sealed class HoverHandler : HoverHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public HoverHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult(document is null ? null : RuleScriptLanguageFeatures.GetHover(document, request.Position));
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}
