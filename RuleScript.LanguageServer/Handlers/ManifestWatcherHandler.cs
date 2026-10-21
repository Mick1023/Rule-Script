using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace RuleScript.LanguageServer.Handlers;

/// <summary>
/// Reloads a workspace folder's engine when its <c>rulescript.json</c> is created, changed, or deleted.
/// </summary>
internal sealed class ManifestWatcherHandler : DidChangeWatchedFilesHandlerBase
{
    private readonly RuleScriptWorkspace _workspace;
    private readonly DiagnosticsPublisher _diagnostics;

    public ManifestWatcherHandler(RuleScriptWorkspace workspace, DiagnosticsPublisher diagnostics)
    {
        _workspace = workspace;
        _diagnostics = diagnostics;
    }

    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        var reloaded = false;
        foreach (var change in request.Changes)
        {
            var path = RuleScriptWorkspace.GetFilePath(change.Uri);
            if (path is not null && string.Equals(Path.GetFileName(path), RuleScriptManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                reloaded |= _workspace.ReloadManifest(path);
            }
        }

        if (reloaded)
        {
            _diagnostics.PublishAll();
        }

        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DidChangeWatchedFilesRegistrationOptions
        {
            Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern($"**/{RuleScriptManifest.FileName}"),
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete
                })
        };
    }
}
