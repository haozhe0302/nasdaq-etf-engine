using Hqqq.ReferenceData.Basket;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Jobs;

/// <summary>
/// Covers the background-job contract: the startup refresh runs once and
/// populates the store before the periodic loop kicks in.
/// </summary>
public class BasketRefreshJobTests
{
    [Fact]
    public async Task OnStartup_RunsSingleRefreshAndPublishes()
    {
        var source = new StubHoldingsSource();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var (job, store, publisher) = BuildJob(source, refreshSeconds: 0, republishSeconds: 0, startupSeconds: 5);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await job.StartAsync(cts.Token);
        await WaitUntilAsync(() => store.Current is not null, cts.Token);
        await job.StopAsync(cts.Token);

        Assert.NotNull(store.Current);
        Assert.Equal(1, source.FetchCount);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task OnStartup_WhenSourceUnavailable_DoesNotCrash()
    {
        var source = new StubHoldingsSource();
        source.Enqueue(HoldingsFetchResult.Unavailable("boom"));

        var (job, store, publisher) = BuildJob(source, refreshSeconds: 0, republishSeconds: 0, startupSeconds: 5);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await job.StartAsync(cts.Token);
        await WaitUntilAsync(() => source.FetchCount >= 1, cts.Token);
        await job.StopAsync(cts.Token);

        Assert.Null(store.Current);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task OnStartup_RealSource_InvokesEnsurePendingBeforeRefresh()
    {
        // When BasketMode=RealSource and a RealSourceBasketPipeline is
        // available, the startup path MUST call EnsurePendingAsync before
        // running RefreshAsync so BasketRefreshPipeline sees a real-source
        // pending basket instead of falling through to the seed.
        //
        // Proof via observable side effect: pre-seed the merged-basket
        // cache on disk. EnsurePendingAsync loads it into PendingBasketStore
        // BEFORE RefreshAsync runs. If the job skipped EnsurePendingAsync,
        // PendingBasketStore would stay empty through startup.
        var cacheDir = Path.Combine(Path.GetTempPath(), $"hqqq-job-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);
        var mergedPath = Path.Combine(cacheDir, "merged.json");

        var seededSnapshot = SnapshotBuilder.Build(count: 60, source: "live:stockanalysis+alphavantage");
        var seededEnvelope = new MergedBasketEnvelope
        {
            Snapshot = seededSnapshot,
            MergedAtUtc = DateTimeOffset.UtcNow,
            TailSource = "alphavantage",
            IsDegraded = false,
            ContentFingerprint16 = "deadbeefcafef00d",
            ConstituentCount = seededSnapshot.Constituents.Count,
            AnchorSource = "stockanalysis",
            HasOfficialShares = true,
            BasketMode = "anchored",
        };
        await File.WriteAllTextAsync(mergedPath,
            System.Text.Json.JsonSerializer.Serialize(seededEnvelope));

        var source = new StubHoldingsSource();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var optionsValue = new ReferenceDataOptions
        {
            Basket = new BasketOptions
            {
                Mode = BasketMode.RealSource,
                Cache = new BasketCacheOptions
                {
                    RawCacheDir = cacheDir,
                    MergedCacheFilePath = mergedPath,
                },
            },
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
            Refresh = new RefreshOptions
            {
                IntervalSeconds = 0,
                RepublishIntervalSeconds = 0,
                StartupMaxWaitSeconds = 5,
            },
        };

        var bench = PipelineBuilder.Build(source: source, options: optionsValue);
        var realSource = BuildRealSourcePipeline(optionsValue, out var pending);

        var job = new BasketRefreshJob(
            bench.Pipeline,
            Options.Create(optionsValue),
            NullLogger<BasketRefreshJob>.Instance,
            realSource);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await job.StartAsync(cts.Token);
        await WaitUntilAsync(() => pending.Pending is not null, cts.Token);
        await job.StopAsync(cts.Token);

        Assert.NotNull(pending.Pending);
        Assert.Equal("deadbeefcafef00d", pending.Pending!.ContentFingerprint16);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(25, ct);
        }
    }

    private static RealSourceBasketPipeline BuildRealSourcePipeline(
        ReferenceDataOptions optionsValue,
        out PendingBasketStore pendingStore)
    {
        var options = Options.Create(optionsValue);
        var rawCache = new RawSourceCache(options, NullLogger<RawSourceCache>.Instance);
        var mergedCache = new MergedBasketCache(options, NullLogger<MergedBasketCache>.Instance);
        pendingStore = new PendingBasketStore();

        var httpFactory = new NoOpHttpFactory();
        var sa = new StockAnalysisBasketAdapter(httpFactory, options, NullLogger<StockAnalysisBasketAdapter>.Instance);
        var sc = new SchwabBasketAdapter(httpFactory, options, NullLogger<SchwabBasketAdapter>.Instance);
        var av = new AlphaVantageBasketAdapter(httpFactory, options, NullLogger<AlphaVantageBasketAdapter>.Instance);
        var nd = new NasdaqBasketAdapter(httpFactory, options, NullLogger<NasdaqBasketAdapter>.Instance);

        return new RealSourceBasketPipeline(
            sa, sc, av, nd, rawCache, mergedCache, pendingStore,
            options,
            new StubEnv("Development"),
            NullLogger<RealSourceBasketPipeline>.Instance);
    }

    private sealed class NoOpHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoOpHandler());
        private sealed class NoOpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public StubEnv(string name) { EnvironmentName = name; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "hqqq-reference-data-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static (BasketRefreshJob Job, ActiveBasketStore Store, CapturingPublisher Publisher) BuildJob(
        StubHoldingsSource source,
        int refreshSeconds,
        int republishSeconds,
        int startupSeconds)
    {
        var optionsValue = new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
            Refresh = new RefreshOptions
            {
                IntervalSeconds = refreshSeconds,
                RepublishIntervalSeconds = republishSeconds,
                StartupMaxWaitSeconds = startupSeconds,
            },
        };

        var bench = PipelineBuilder.Build(source: source, options: optionsValue);

        var job = new BasketRefreshJob(bench.Pipeline, Options.Create(optionsValue), NullLogger<BasketRefreshJob>.Instance);
        return (job, bench.Store, bench.Publisher);
    }
}
