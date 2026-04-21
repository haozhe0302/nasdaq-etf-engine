using Hqqq.ReferenceData.Basket;
using Hqqq.ReferenceData.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hqqq.ReferenceData.Tests.Basket;

/// <summary>
/// Covers the four-source anchored pipeline: anchor selection
/// (StockAnalysis vs Schwab by AsOfDate, tie → StockAnalysis),
/// universe guardrail via Nasdaq, and the explicit anchor-less
/// proxy posture that is only accepted outside Production or with
/// <c>AllowAnchorlessProxyInProduction=true</c>.
/// </summary>
public class RealSourceBasketPipelineTests
{
    private static readonly DateOnly OlderDate = new(2026, 4, 15);
    private static readonly DateOnly NewerDate = new(2026, 4, 17);

    [Fact]
    public async Task MergeAsync_StockAnalysisNewer_AnchorIsStockAnalysis()
    {
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: StockAnalysisResult(date: NewerDate, shares: 500m),
            schwab: SchwabResult(date: OlderDate, shares: 400m),
            alpha: AlphaResult(),
            nasdaq: NasdaqResult());

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("stockanalysis", outcome.Envelope!.AnchorSource);
        Assert.True(outcome.Envelope.HasOfficialShares);
        Assert.False(outcome.Envelope.IsDegraded);
        Assert.Equal("anchored", outcome.Envelope.BasketMode);
    }

    [Fact]
    public async Task MergeAsync_SchwabNewer_AnchorIsSchwab()
    {
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: StockAnalysisResult(date: OlderDate, shares: 500m),
            schwab: SchwabResult(date: NewerDate, shares: 400m),
            alpha: AlphaResult(),
            nasdaq: NasdaqResult());

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("schwab", outcome.Envelope!.AnchorSource);
    }

    [Fact]
    public async Task MergeAsync_TieOnAsOfDate_AnchorIsStockAnalysis()
    {
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: StockAnalysisResult(date: NewerDate, shares: 500m),
            schwab: SchwabResult(date: NewerDate, shares: 400m),
            alpha: AlphaResult(),
            nasdaq: NasdaqResult());

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("stockanalysis", outcome.Envelope!.AnchorSource);
    }

    [Fact]
    public async Task MergeAsync_AnchorAvailable_NasdaqUniverseGuardrailFiltersTail()
    {
        // AlphaVantage returns AAPL (anchor dup — dropped) + BOGUS (not in
        // universe — dropped) + MSFT (in universe — kept).
        var alpha = new AlphaVantageBasketAdapter.RawResult
        {
            Holdings = new[]
            {
                new AlphaVantageBasketAdapter.AlphaHolding("AAPL", "Apple dup", 5m),
                new AlphaVantageBasketAdapter.AlphaHolding("BOGUS", "NotInUniverse", 5m),
                new AlphaVantageBasketAdapter.AlphaHolding("MSFT", "Microsoft", 3m),
            },
            Sectors = Array.Empty<AlphaVantageBasketAdapter.AlphaSector>(),
            NetAssets = 0m,
            RawCount = 3,
            FilteredCount = 3,
        };

        var pipeline = await BuildPipelineAsync(
            stockAnalysis: StockAnalysisResult(date: NewerDate, shares: 100m),
            schwab: null,
            alpha: alpha,
            nasdaq: new NasdaqBasketAdapter.RawResult
            {
                Entries = new[]
                {
                    new NasdaqBasketAdapter.NasdaqEntry("AAPL", "Apple", 3000m, 60m),
                    new NasdaqBasketAdapter.NasdaqEntry("MSFT", "Microsoft", 2000m, 40m),
                },
                TotalMarketCap = 5000m,
            });

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        var symbols = outcome.Envelope!.Snapshot.Constituents.Select(c => c.Symbol).ToHashSet();
        Assert.Contains("AAPL", symbols);  // anchor
        Assert.Contains("MSFT", symbols);  // tail in universe
        Assert.DoesNotContain("BOGUS", symbols);
    }

    [Fact]
    public async Task MergeAsync_NoAnchor_Development_FallsThroughToAnchorLessProxy()
    {
        // No StockAnalysis, no Schwab, no AlphaVantage → Nasdaq proxy
        // tail without an anchor. Outside Production this is accepted
        // as an explicit degraded posture.
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: null,
            schwab: null,
            alpha: null,
            nasdaq: NasdaqResult(),
            environment: "Development");

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Null(outcome.Envelope!.AnchorSource);
        Assert.True(outcome.Envelope.IsDegraded);
        Assert.False(outcome.Envelope.HasOfficialShares);
        Assert.Equal("anchor-less-proxy", outcome.Envelope.BasketMode);
    }

    [Fact]
    public async Task MergeAsync_NoAnchor_Production_WithoutOptIn_RefusesToEmit()
    {
        // Production + AllowAnchorlessProxyInProduction=false: the
        // anchor-less proxy path is refused. No pending basket is set;
        // the caller must treat MergeOutcome.AnchorRequired as "remain
        // not-ready" and surface that through the health check.
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: null,
            schwab: null,
            alpha: null,
            nasdaq: NasdaqResult(),
            environment: "Production",
            basket: new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowAnchorlessProxyInProduction = false,
                Sources = new BasketSourcesOptions
                {
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            });

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Null(outcome.Envelope);
        Assert.Equal(RealSourceBasketPipeline.MergeOutcome.AnchorRequired.Reason, outcome.Reason);
        Assert.Null(pipeline.Pending.Pending);
    }

    [Fact]
    public async Task MergeAsync_NoAnchor_Production_WithOptIn_EmitsDegradedProxy()
    {
        var pipeline = await BuildPipelineAsync(
            stockAnalysis: null,
            schwab: null,
            alpha: null,
            nasdaq: NasdaqResult(),
            environment: "Production",
            basket: new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowAnchorlessProxyInProduction = true,
                Sources = new BasketSourcesOptions
                {
                    Nasdaq = new NasdaqSourceOptions { Enabled = true },
                },
            });

        var outcome = await pipeline.Pipeline.MergeAsync(CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.True(outcome.Envelope!.IsDegraded);
        Assert.Null(outcome.Envelope.AnchorSource);
    }

    private static StockAnalysisBasketAdapter.RawResult StockAnalysisResult(DateOnly date, decimal shares)
        => new()
        {
            Holdings = new[]
            {
                new StockAnalysisBasketAdapter.ParsedHolding("AAPL", "Apple", 9m, shares),
            },
            AsOfDate = date,
            TotalReported = 100,
        };

    private static SchwabBasketAdapter.RawResult SchwabResult(DateOnly date, decimal shares)
        => new()
        {
            Holdings = new[]
            {
                new SchwabBasketAdapter.ParsedHolding("AAPL", "Apple", 9m, shares, 100_000m),
            },
            AsOfDate = date,
            TotalReported = 20,
        };

    private static AlphaVantageBasketAdapter.RawResult AlphaResult() => new()
    {
        Holdings = new[]
        {
            new AlphaVantageBasketAdapter.AlphaHolding("MSFT", "Microsoft", 5m),
        },
        Sectors = Array.Empty<AlphaVantageBasketAdapter.AlphaSector>(),
        NetAssets = 0m,
        RawCount = 1,
        FilteredCount = 1,
    };

    private static NasdaqBasketAdapter.RawResult NasdaqResult() => new()
    {
        Entries = new[]
        {
            new NasdaqBasketAdapter.NasdaqEntry("AAPL", "Apple", 3000m, 60m),
            new NasdaqBasketAdapter.NasdaqEntry("MSFT", "Microsoft", 2000m, 40m),
        },
        TotalMarketCap = 5000m,
    };

    private static async Task<Bench> BuildPipelineAsync(
        StockAnalysisBasketAdapter.RawResult? stockAnalysis,
        SchwabBasketAdapter.RawResult? schwab,
        AlphaVantageBasketAdapter.RawResult? alpha,
        NasdaqBasketAdapter.RawResult? nasdaq,
        string environment = "Development",
        BasketOptions? basket = null)
    {
        basket ??= new BasketOptions
        {
            Mode = BasketMode.RealSource,
            Sources = new BasketSourcesOptions
            {
                StockAnalysis = new StockAnalysisSourceOptions { Enabled = true },
                Schwab = new SchwabSourceOptions { Enabled = true },
                AlphaVantage = new AlphaVantageSourceOptions { Enabled = true, ApiKey = "real-key" },
                Nasdaq = new NasdaqSourceOptions { Enabled = true },
            },
        };

        // Use a temp dir for on-disk caches so runs stay isolated.
        var cacheDir = Path.Combine(Path.GetTempPath(), $"hqqq-pipeline-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);

        basket.Cache = new BasketCacheOptions
        {
            RawCacheDir = cacheDir,
            MergedCacheFilePath = Path.Combine(cacheDir, "merged.json"),
        };

        var options = Options.Create(new ReferenceDataOptions
        {
            Basket = basket,
        });

        var rawCache = new RawSourceCache(options, NullLogger<RawSourceCache>.Instance);
        var mergedCache = new MergedBasketCache(options, NullLogger<MergedBasketCache>.Instance);
        var pending = new PendingBasketStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero));

        // Pre-populate the raw cache with whatever the caller provided.
        if (stockAnalysis is not null)
            await rawCache.WriteAsync(StockAnalysisBasketAdapter.AdapterName, stockAnalysis, CancellationToken.None);
        if (schwab is not null)
            await rawCache.WriteAsync(SchwabBasketAdapter.AdapterName, schwab, CancellationToken.None);
        if (alpha is not null)
            await rawCache.WriteAsync(AlphaVantageBasketAdapter.AdapterName, alpha, CancellationToken.None);
        if (nasdaq is not null)
            await rawCache.WriteAsync(NasdaqBasketAdapter.AdapterName, nasdaq, CancellationToken.None);

        // Adapters themselves are not invoked in MergeAsync-only tests,
        // but DI still wants instances.
        var httpFactory = new NoOpHttpClientFactory();
        var sa = new StockAnalysisBasketAdapter(httpFactory, options, NullLogger<StockAnalysisBasketAdapter>.Instance);
        var sc = new SchwabBasketAdapter(httpFactory, options, NullLogger<SchwabBasketAdapter>.Instance);
        var av = new AlphaVantageBasketAdapter(httpFactory, options, NullLogger<AlphaVantageBasketAdapter>.Instance);
        var nd = new NasdaqBasketAdapter(httpFactory, options, NullLogger<NasdaqBasketAdapter>.Instance);

        var pipeline = new RealSourceBasketPipeline(
            sa, sc, av, nd,
            rawCache, mergedCache, pending,
            options,
            new StubEnvironment(environment),
            NullLogger<RealSourceBasketPipeline>.Instance,
            clock);

        return new Bench(pipeline, pending, cacheDir);
    }

    private sealed record Bench(RealSourceBasketPipeline Pipeline, PendingBasketStore Pending, string CacheDir);

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoOpHandler());
        private sealed class NoOpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public StubEnvironment(string name) { EnvironmentName = name; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "hqqq-reference-data-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
