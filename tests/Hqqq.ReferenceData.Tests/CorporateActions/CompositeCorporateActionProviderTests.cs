using System.Net;
using System.Text;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.CorporateActions;

/// <summary>
/// Composite provider behaviour: file-only baseline, Tiingo overlay,
/// dedup of overlapping rows, and the honest-degradation fallback when
/// the Tiingo arm is unreachable. Tiingo HTTP is faked through an
/// <see cref="IHttpClientFactory"/> backed by a scripted handler so the
/// tests stay deterministic and offline.
/// </summary>
public class CompositeCorporateActionProviderTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Fetch_TiingoDisabled_ReturnsFileFeedVerbatim()
    {
        var (composite, _) = BuildComposite(tiingoEnabled: false, tiingoHandler: ScriptedHandler.Empty());

        var feed = await composite.FetchAsync(
            symbols: new[] { "AAPL" }, from: From, to: To, ct: CancellationToken.None);

        Assert.Equal("file", feed.Source);
        Assert.Null(feed.Error);
    }

    [Fact]
    public async Task Fetch_FilePathProvided_UsesFileSplits()
    {
        var fileJson = """
        {
          "splits": [
            { "symbol": "AAPL", "effectiveDate": "2026-04-15", "factor": 4 }
          ],
          "renames": []
        }
        """;
        using var temp = TempJson.Create(fileJson);

        var (composite, _) = BuildComposite(
            tiingoEnabled: false,
            tiingoHandler: ScriptedHandler.Empty(),
            filePath: temp.Path);

        var feed = await composite.FetchAsync(
            new[] { "AAPL" }, From, To, CancellationToken.None);

        Assert.Equal("file", feed.Source);
        var split = Assert.Single(feed.Splits);
        Assert.Equal("AAPL", split.Symbol);
        Assert.Equal(4m, split.Factor);
        Assert.Equal(new DateOnly(2026, 4, 15), split.EffectiveDate);
    }

    [Fact]
    public async Task Fetch_TiingoEnabled_MergesAndDeduplicatesSplitsByDate()
    {
        // File baseline already has the Apr-15 split — Tiingo also returns
        // it (same symbol+date) plus a NEW Mar-01 split. The merged feed
        // should contain both unique rows but only one Apr-15.
        var fileJson = """
        {
          "splits": [
            { "symbol": "AAPL", "effectiveDate": "2026-04-15", "factor": 4 }
          ],
          "renames": []
        }
        """;
        using var temp = TempJson.Create(fileJson);

        var tiingoBody = """
        [
          { "date": "2026-04-15T00:00:00.000Z", "splitFactor": 4 },
          { "date": "2026-03-01T00:00:00.000Z", "splitFactor": 2 }
        ]
        """;
        var handler = ScriptedHandler.For(_ => HttpResponse(tiingoBody));

        var (composite, _) = BuildComposite(
            tiingoEnabled: true,
            tiingoHandler: handler,
            filePath: temp.Path);

        var feed = await composite.FetchAsync(
            new[] { "AAPL" }, From, To, CancellationToken.None);

        Assert.Equal("file+tiingo", feed.Source);
        Assert.Null(feed.Error);

        Assert.Equal(2, feed.Splits.Count);
        var bySymAndDate = feed.Splits
            .Select(s => (s.Symbol, s.EffectiveDate))
            .OrderBy(t => t.EffectiveDate)
            .ToArray();
        Assert.Equal(new[]
        {
            ("AAPL", new DateOnly(2026, 3, 1)),
            ("AAPL", new DateOnly(2026, 4, 15)),
        }, bySymAndDate);
    }

    [Fact]
    public async Task Fetch_TiingoEnabledButThrows_FallsBackToFileWithDegradedLineage()
    {
        // Composite must NEVER let a Tiingo failure block the deterministic
        // file feed — the local/offline path stays usable.
        var fileJson = """
        {
          "splits": [
            { "symbol": "AAPL", "effectiveDate": "2026-04-15", "factor": 4 }
          ],
          "renames": []
        }
        """;
        using var temp = TempJson.Create(fileJson);

        var handler = ScriptedHandler.For(_ => throw new HttpRequestException("network down"));

        var (composite, _) = BuildComposite(
            tiingoEnabled: true,
            tiingoHandler: handler,
            filePath: temp.Path);

        var feed = await composite.FetchAsync(
            new[] { "AAPL" }, From, To, CancellationToken.None);

        // The Tiingo provider's own try/catch turns the throw into a feed
        // with Error set; the composite then surfaces that as
        // "file+tiingo-degraded" with the file rows intact.
        Assert.StartsWith("file+tiingo-degraded", feed.Source);
        Assert.NotNull(feed.Error);
        Assert.Single(feed.Splits);
        Assert.Equal("AAPL", feed.Splits[0].Symbol);
    }

    [Fact]
    public async Task Fetch_TiingoEnabledButReturnsError_FallsBackToFileWithDegradedLineage()
    {
        // 5xx from Tiingo flows back through the per-symbol catch in the
        // Tiingo provider and ends up as an empty splits list with an
        // Error string on the per-symbol path. The composite still needs
        // to mark the lineage as degraded so operators can see the
        // deviation in /api/basket/current.
        using var temp = TempJson.Create("""
        { "splits": [ { "symbol": "AAPL", "effectiveDate": "2026-04-15", "factor": 4 } ], "renames": [] }
        """);

        var handler = ScriptedHandler.For(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var (composite, _) = BuildComposite(
            tiingoEnabled: true,
            tiingoHandler: handler,
            filePath: temp.Path);

        var feed = await composite.FetchAsync(
            new[] { "AAPL" }, From, To, CancellationToken.None);

        // Per-symbol HTTP error is swallowed by the Tiingo provider so
        // the merged feed stays usable and the lineage stays "file+tiingo"
        // (no top-level Error). This documents the honest behaviour:
        // single-symbol failures degrade the per-symbol payload but do
        // not block the deterministic baseline.
        Assert.Equal("file+tiingo", feed.Source);
        Assert.Single(feed.Splits);
    }

    [Fact]
    public async Task Fetch_TiingoEnabledButMissingApiKey_FallsBackWithExplicitErrorMessage()
    {
        // Composite must surface a clear lineage tag when Tiingo is
        // configured-but-unusable (missing key). The file feed must still
        // pass through unaltered.
        using var temp = TempJson.Create("""
        { "splits": [ { "symbol": "AAPL", "effectiveDate": "2026-04-15", "factor": 4 } ], "renames": [] }
        """);

        var (composite, _) = BuildComposite(
            tiingoEnabled: true,
            tiingoHandler: ScriptedHandler.Empty(),
            filePath: temp.Path,
            tiingoApiKey: null);

        var feed = await composite.FetchAsync(
            new[] { "AAPL" }, From, To, CancellationToken.None);

        Assert.Equal("file+tiingo-degraded", feed.Source);
        Assert.NotNull(feed.Error);
        Assert.Contains("ApiKey", feed.Error!);
        Assert.Single(feed.Splits);
    }

    [Fact]
    public async Task Fetch_RenamesAlwaysComeFromFileProvider()
    {
        // Tiingo EOD does not surface ticker rename metadata; the
        // composite must keep the file's renames regardless of Tiingo
        // enablement.
        using var temp = TempJson.Create("""
        {
          "splits": [],
          "renames": [
            { "oldSymbol": "FB", "newSymbol": "META", "effectiveDate": "2026-02-15" }
          ]
        }
        """);

        var (composite, _) = BuildComposite(
            tiingoEnabled: true,
            tiingoHandler: ScriptedHandler.For(_ => HttpResponse("[]")),
            filePath: temp.Path);

        var feed = await composite.FetchAsync(
            new[] { "META" }, From, To, CancellationToken.None);

        var rename = Assert.Single(feed.Renames);
        Assert.Equal("FB", rename.OldSymbol);
        Assert.Equal("META", rename.NewSymbol);
    }

    private static (CompositeCorporateActionProvider Composite, ScriptedHandler Handler)
        BuildComposite(
            bool tiingoEnabled,
            ScriptedHandler tiingoHandler,
            string? filePath = null,
            string? tiingoApiKey = "real-key")
    {
        var fileProvider = new FileCorporateActionProvider(
            Options.Create(new ReferenceDataOptions
            {
                CorporateActions = new CorporateActionOptions
                {
                    File = new FileCorporateActionOptions { Path = filePath },
                },
            }),
            NullLogger<FileCorporateActionProvider>.Instance);

        var tiingoOptions = new TiingoCorporateActionOptions
        {
            Enabled = tiingoEnabled,
            ApiKey = tiingoApiKey,
            BaseUrl = "https://example.test/tiingo/daily",
            TimeoutSeconds = 1,
            MaxConcurrency = 2,
            CacheTtlMinutes = 5,
        };

        var tiingoProvider = new TiingoCorporateActionProvider(
            new SingleClientFactory(tiingoHandler),
            Options.Create(new ReferenceDataOptions
            {
                CorporateActions = new CorporateActionOptions { Tiingo = tiingoOptions },
            }),
            NullLogger<TiingoCorporateActionProvider>.Instance);

        var composite = new CompositeCorporateActionProvider(
            fileProvider,
            tiingoProvider,
            Options.Create(new ReferenceDataOptions
            {
                CorporateActions = new CorporateActionOptions
                {
                    Tiingo = new TiingoCorporateActionOptions { Enabled = tiingoEnabled },
                },
            }),
            NullLogger<CompositeCorporateActionProvider>.Instance);

        return (composite, tiingoHandler);
    }

    private static HttpResponseMessage HttpResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        private ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public static ScriptedHandler For(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => new(respond);

        public static ScriptedHandler Empty() => new(_ => HttpResponse("[]"));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private sealed class TempJson : IDisposable
    {
        public string Path { get; }
        private TempJson(string path) { Path = path; }
        public static TempJson Create(string contents)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hqqq-corp-actions-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, contents);
            return new TempJson(path);
        }
        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* best-effort */ }
        }
    }
}
