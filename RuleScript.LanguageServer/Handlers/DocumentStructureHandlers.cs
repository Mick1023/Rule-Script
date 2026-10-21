using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RuleScript.LanguageServer.Handlers;

internal sealed class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public DocumentSymbolHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request,
        CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        if (document is null)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        var symbols = RuleScriptLanguageFeatures.GetDocumentSymbols(document)
            .Select(symbol => new SymbolInformationOrDocumentSymbol(symbol));
        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}

internal sealed class FoldingRangeHandler : FoldingRangeHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public FoldingRangeHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(request.TextDocument.Uri);
        return Task.FromResult(document is null
            ? null
            : new Container<FoldingRange>(RuleScriptLanguageFeatures.GetFoldingRanges(document)));
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new FoldingRangeRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}

internal sealed class DocumentFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;

    public DocumentFormattingHandler(RuleScriptWorkspace workspace)
    {
        _workspace = workspace;
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        var text = _workspace.GetText(request.TextDocument.Uri);
        return Task.FromResult(text is null
            ? null
            : new TextEditContainer(RuleScriptLanguageFeatures.Format(text)));
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions { DocumentSelector = RuleScriptDocumentSelector.Instance };
    }
}
