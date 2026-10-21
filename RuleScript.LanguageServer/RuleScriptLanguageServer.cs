using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
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
            .WithServices(services => services
                .AddSingleton(workspace)
                .AddSingleton<DiagnosticsPublisher>())
            .WithHandler<TextDocumentSyncHandler>()
            .WithHandler<DefinitionHandler>()
            .WithHandler<ReferencesHandler>()
            .WithHandler<HoverHandler>()
            .WithHandler<DocumentSymbolHandler>()
            .WithHandler<FoldingRangeHandler>()
            .WithHandler<DocumentFormattingHandler>()
            .WithHandler<CompletionHandler>()
            .WithHandler<SignatureHelpHandler>()
            .WithHandler<RenameHandler>()
            .WithHandler<PrepareRenameHandler>()
            .WithHandler<ManifestWatcherHandler>()
            .OnInitialize((server, request, _) =>
            {
                workspace.Warn = message => server.Window.LogWarning(message);

                // Every workspace folder gets its own engine; older clients only send a root URI.
                var roots = request.WorkspaceFolders?.Select(folder => folder.Uri).ToArray() ?? [];
                workspace.Initialize(roots.Length > 0 ? roots : request.RootUri is { } root ? [root] : []);
                return Task.CompletedTask;
            });
    }
}
