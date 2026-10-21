using System.IO.Pipelines;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RuleScript.LanguageServer;

namespace RuleScript.LanguageServer.Tests;

/// <summary>
/// Drives the real server over in-memory JSON-RPC pipes to verify handler registration and wiring.
/// </summary>
public sealed class LanguageServerProtocolTests : IAsyncLifetime
{
    private static readonly DocumentUri Uri = DocumentUri.From("untitled:main.rules");

    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly List<PublishDiagnosticsParams> _published = [];
    private readonly SemaphoreSlim _publishedSignal = new(0);
    private OmniSharp.Extensions.LanguageServer.Server.LanguageServer? _server;
    private LanguageClient? _client;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serverTask = RuleScriptLanguageServer.StartAsync(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream(),
            timeout.Token);

        _client = LanguageClient.Create(options => options
            .WithInput(_serverToClient.Reader)
            .WithOutput(_clientToServer.Writer)
            .OnPublishDiagnostics(request =>
            {
                lock (_published)
                {
                    _published.Add(request);
                }

                _publishedSignal.Release();
            }));

        // OmniSharp waits forever when server initialization throws, so bound it to surface the failure.
        await Task.WhenAll(_client.Initialize(timeout.Token), serverTask).WaitAsync(TimeSpan.FromSeconds(30));
        _server = await serverTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            try
            {
                // A failed test must not hang here waiting for a server that stopped responding.
                await _client.Shutdown().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
            }

            _client.Dispose();
        }

        _server?.Dispose();
    }

    [Fact]
    public async Task Server_AdvertisesMvpCapabilities()
    {
        var capabilities = _client!.ServerSettings.Capabilities;

        Assert.NotNull(capabilities.TextDocumentSync);
        Assert.NotNull(capabilities.DefinitionProvider);
        Assert.NotNull(capabilities.ReferencesProvider);
        Assert.NotNull(capabilities.HoverProvider);
        Assert.NotNull(capabilities.DocumentSymbolProvider);
        Assert.NotNull(capabilities.FoldingRangeProvider);
        Assert.NotNull(capabilities.DocumentFormattingProvider);
        Assert.Contains(".", capabilities.CompletionProvider!.TriggerCharacters!);
        Assert.Contains("(", capabilities.SignatureHelpProvider!.TriggerCharacters!);
        Assert.True(capabilities.RenameProvider!.Value!.PrepareProvider);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OpenDocument_PublishesDiagnosticsAndAnswersRequests()
    {
        const string source = """
            /// Adds one.
            function AddOne(value: number) -> number:
                return value + 1;
            endfunction

            var total = AddOne(1);
            var broken = ;
            """;

        _client!.TextDocument.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = Uri,
                LanguageId = "rulescript",
                Version = 1,
                Text = source
            }
        });

        var diagnostics = await WaitForDiagnosticsAsync(_ => true);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal(6, diagnostic.Range.Start.Line);

        var hover = await _client.TextDocument.RequestHover(new HoverParams
        {
            TextDocument = Uri,
            Position = new Position(5, 13)
        });
        Assert.Contains("function AddOne(value: number) -> number", hover!.Contents.MarkupContent!.Value);

        var definition = await _client.TextDocument.RequestDefinition(new DefinitionParams
        {
            TextDocument = Uri,
            Position = new Position(5, 13)
        });
        var location = Assert.Single(definition!).Location!;
        Assert.Equal(1, location.Range.Start.Line);

        var symbols = await _client.TextDocument.RequestDocumentSymbol(new DocumentSymbolParams { TextDocument = Uri });
        Assert.Contains(symbols!, symbol => symbol.DocumentSymbol?.Name == "AddOne");

        var folding = await _client.TextDocument.RequestFoldingRange(new FoldingRangeRequestParam { TextDocument = Uri });
        Assert.Contains(folding!, range => range.StartLine == 1);

        var completion = await _client.TextDocument.RequestCompletion(new CompletionParams
        {
            TextDocument = Uri,
            Position = new Position(5, 0)
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(completion, item => item.Label == "AddOne");

        var signatureHelp = await _client.TextDocument.RequestSignatureHelp(new SignatureHelpParams
        {
            TextDocument = Uri,
            Position = new Position(5, 19)
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("AddOne(value: number) -> number", Assert.Single(signatureHelp!.Signatures).Label);
    }

    [Fact]
    public async Task ChangeDocument_RepublishesDiagnosticsForLatestText()
    {
        _client!.TextDocument.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = Uri, LanguageId = "rulescript", Version = 1, Text = "var a = ;" }
        });
        Assert.NotEmpty((await WaitForDiagnosticsAsync(_ => true)).Diagnostics);

        _client.TextDocument.DidChangeTextDocument(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = Uri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = "var a = 1;" })
        });

        var latest = await WaitForDiagnosticsAsync(request => request.Version == 2);
        Assert.Empty(latest.Diagnostics);
    }

    [Fact]
    public async Task Rename_ReturnsEditsAndReportsRejectionReason()
    {
        _client!.TextDocument.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = Uri,
                LanguageId = "rulescript",
                Version = 1,
                Text = "var count = 1;\ncount = count + Length(\"x\");"
            }
        });
        await WaitForDiagnosticsAsync(_ => true);

        var prepare = await _client
            .SendRequest("textDocument/prepareRename", new PrepareRenameParams { TextDocument = Uri, Position = new Position(0, 5) })
            .Returning<RangeOrPlaceholderRange?>(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("count", prepare!.PlaceholderRange!.Placeholder);

        var edit = await _client.TextDocument.RequestRename(new RenameParams
        {
            TextDocument = Uri,
            Position = new Position(0, 5),
            NewName = "total"
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, Assert.Single(edit!.Changes!).Value.Count());

        var rejection = await Assert.ThrowsAnyAsync<Exception>(() => _client.TextDocument.RequestRename(new RenameParams
        {
            TextDocument = Uri,
            Position = new Position(1, 17),
            NewName = "Size"
        }).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("Host Functions cannot be renamed", rejection.ToString());
    }

    private async Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(Func<PublishDiagnosticsParams, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            lock (_published)
            {
                var match = _published.LastOrDefault(request => request.Uri == Uri && predicate(request));
                if (match is not null)
                {
                    return match;
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !await _publishedSignal.WaitAsync(remaining))
            {
                throw new TimeoutException("Timed out waiting for published diagnostics.");
            }
        }
    }
}
