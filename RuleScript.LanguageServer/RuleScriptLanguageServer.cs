using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;
using RuleScript.LanguageServer.Handlers;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RuleScript.LanguageServer;

/// <summary>
/// Configures and starts the RuleScript language server over the supplied streams.
/// </summary>
internal static class RuleScriptLanguageServer
{
    public static Task<OmniSharpLanguageServer> StartAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        return OmniSharpLanguageServer.From(options => Configure(options, input, output), cancellationToken);
    }

    public static void Configure(LanguageServerOptions options, Stream input, Stream output)
    {
        var workspace = new RuleScriptWorkspace();

        options
            .WithInput(input)
            .WithOutput(output)
            .WithServerInfo(new OmniSharp.Extensions.LanguageServer.Protocol.Models.ServerInfo
            {
                Name = "RuleScript Language Server",
                Version = typeof(RuleScriptLanguageServer).Assembly.GetName().Version?.ToString()
            })
            .WithServices(services => services.AddSingleton(workspace))
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<DefinitionHandler>()
            .WithHandler<ReferencesHandler>()
            .WithHandler<HoverHandler>()
            .WithHandler<DocumentSymbolHandler>()
            .WithHandler<FoldingRangeHandler>()
            .WithHandler<DocumentFormattingHandler>()
            .OnInitialize((_, request, _) =>
            {
                var root = request.WorkspaceFolders?.FirstOrDefault()?.Uri ?? request.RootUri;
                workspace.Initialize(root);
                return Task.CompletedTask;
            });
    }
}
