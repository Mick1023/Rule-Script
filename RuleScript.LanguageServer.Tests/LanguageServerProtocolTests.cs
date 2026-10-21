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

        await Task.WhenAll(_client.Initialize(timeout.Token), serverTask);
        _server = await serverTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.Shutdown();
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
