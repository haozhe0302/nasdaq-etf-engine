using Hqqq.Infrastructure.Redis;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;
using Hqqq.QuoteEngine.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Workers;

public class QuoteEngineWorkerPublishingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private sealed record Rig(
        QuoteEngineWorker Worker,
        FakeBasketStateFeed BasketFeed,
        FakeRawTickFeed TickFeed,
        InMemoryRedisStringCache Cache,
        RecordingPricingSnapshotProducer Producer,
        RecordingQuoteUpdatePublisher QuoteUpdatePublisher,
        FakeSystemClock Clock,
        string CheckpointPath);

    private static Rig BuildRig(string tempDir)
    {
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions
        {
            StaleAfter = TimeSpan.FromSeconds(30),
            AnchorSymbol = "QQQ",
            MaterializeInterval = TimeSpan.FromMilliseconds(10),
            CheckpointInterval = TimeSpan.FromMinutes(10), // keep checkpoints out of the way
            CheckpointPath = Path.Combine(tempDir, "checkpoint.json"),
        };

        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var runtime = new EngineRuntimeState(options.SeriesCapacity);
        var calculator = new IncrementalNavCalculator(quotes, baskets, runtime, clock, options);
        var snap = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
        var delta = new QuoteDeltaMaterializer(baskets, runtime, snap, clock);
        var engine = new Hqqq.QuoteEngine.Services.QuoteEngine(
            quotes, baskets, runtime, calculator, snap, delta);

        var cache = new InMemoryRedisStringCache();
        var producer = new RecordingPricingSnapshotProducer();
        var quoteSink = new RedisSnapshotWriter(cache);
        var constituentsSink = new RedisConstituentsWriter(cache);
        var publisher = new SnapshotTopicPublisher(producer, options);
        var quoteUpdatePublisher = new RecordingQuoteUpdatePublisher();
        var constituentsMat = new ConstituentsSnapshotMaterializer(quotes, baskets, clock, options);
        var eventMapper = new QuoteSnapshotV1Mapper(quotes, baskets, clock);

        var basketFeed = new FakeBasketStateFeed();
        var tickFeed = new FakeRawTickFeed();
        var store = new FileEngineCheckpointStore(
            options.CheckpointPath, NullLogger<FileEngineCheckpointStore>.Instance);

        var worker = new QuoteEngineWorker(
            engine, tickFeed, basketFeed, options, store,
            quoteSink, constituentsSink, publisher, quoteUpdatePublisher,
            constituentsMat, eventMapper,
            NullLogger<QuoteEngineWorker>.Instance);

        return new Rig(worker, basketFeed, tickFeed, cache, producer, quoteUpdatePublisher, clock, options.CheckpointPath);
    }

    private static ActiveBasket SampleBasket() =>
        new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-publish-1")
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 0.5m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.5m)
            .Build();

    private static async Task WaitForAsync(
        Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Worker_PublishesSnapshotToRedisAndKafka_OnMaterializeCycle()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rig = BuildRig(tempDir);
            var basket = SampleBasket();

            rig.BasketFeed.Enqueue(basket);
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("AAPL", 205m, rig.Clock.UtcNow, previousClose: 200m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("MSFT", 402m, rig.Clock.UtcNow, previousClose: 400m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rig.Worker.StartAsync(cts.Token);

            await WaitForAsync(
                () => rig.Producer.Published.Count > 0
                    && rig.Cache.Values.ContainsKey(RedisKeys.Snapshot(basket.BasketId))
                    && rig.Cache.Values.ContainsKey(RedisKeys.Constituents(basket.BasketId)),
                TimeSpan.FromSeconds(5));

            rig.BasketFeed.Complete();
            rig.TickFeed.Complete();
            await rig.Worker.StopAsync(cts.Token);

            Assert.True(rig.Cache.Values.ContainsKey("hqqq:snapshot:HQQQ"),
                "Expected Redis snapshot key to be populated");
            Assert.True(rig.Cache.Values.ContainsKey("hqqq:constituents:HQQQ"),
                "Expected Redis constituents key to be populated");

            var published = rig.Producer.Published.First();
            Assert.Equal("pricing.snapshots.v1", published.Topic);
            Assert.Equal("HQQQ", published.Key);
            Assert.True(published.Value.Nav > 0m);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Worker_NeverWritesRawTickShapedKeysToRedis()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rig = BuildRig(tempDir);
            var basket = SampleBasket();

            rig.BasketFeed.Enqueue(basket);

            // Drive 100 ticks through the pipeline. If the worker ever
            // regressed to using Redis as raw event storage, this would
            // explode the write count and surface a non-snapshot-shaped key.
            for (int i = 0; i < 100; i++)
            {
                rig.TickFeed.Enqueue(TestBasketBuilder.Tick("AAPL", 200m + i * 0.01m, rig.Clock.UtcNow));
                rig.TickFeed.Enqueue(TestBasketBuilder.Tick("MSFT", 400m + i * 0.01m, rig.Clock.UtcNow));
                rig.TickFeed.Enqueue(TestBasketBuilder.Tick("QQQ", 500m + i * 0.01m, rig.Clock.UtcNow));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rig.Worker.StartAsync(cts.Token);

            await WaitForAsync(
                () => rig.Cache.Values.ContainsKey(RedisKeys.Snapshot(basket.BasketId)),
                TimeSpan.FromSeconds(5));

            rig.BasketFeed.Complete();
            rig.TickFeed.Complete();
            await rig.Worker.StopAsync(cts.Token);

            // Only the two snapshot patterns are allowed — no raw-tick /
            // latest-price keys should exist.
            foreach (var key in rig.Cache.Values.Keys)
            {
                Assert.True(
                    key.StartsWith("hqqq:snapshot:", StringComparison.Ordinal)
                    || key.StartsWith("hqqq:constituents:", StringComparison.Ordinal),
                    $"Unexpected Redis key written by worker: {key}");
            }

            // Writes track the 1 Hz materialize cycle, not per-tick volume.
            // Giving the test ~ (5 s / 10 ms) = 500 potential cycles as a
            // very generous upper bound still keeps us far below 300 ticks.
            Assert.True(
                rig.Cache.Writes.Count < 300,
                $"Redis writes ({rig.Cache.Writes.Count}) should track materialize cycles, not tick volume");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Worker_PublishesQuoteUpdate_AfterSnapshotMaterializes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rig = BuildRig(tempDir);
            var basket = SampleBasket();

            rig.BasketFeed.Enqueue(basket);
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("AAPL", 205m, rig.Clock.UtcNow, previousClose: 200m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("MSFT", 402m, rig.Clock.UtcNow, previousClose: 400m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rig.Worker.StartAsync(cts.Token);

            await WaitForAsync(
                () => rig.QuoteUpdatePublisher.Published.Count > 0,
                TimeSpan.FromSeconds(5));

            rig.BasketFeed.Complete();
            rig.TickFeed.Complete();
            await rig.Worker.StopAsync(cts.Token);

            // Phase 2D2 — publish must be tied to a successful snapshot
            // write (so REST bootstrap can recover the same state) and
            // must use the basket id of the active basket.
            Assert.NotEmpty(rig.QuoteUpdatePublisher.Published);
            var first = rig.QuoteUpdatePublisher.Published.First();
            Assert.Equal("HQQQ", first.BasketId);
            Assert.True(first.Update.Nav > 0m);
            Assert.True(rig.Cache.Values.ContainsKey(RedisKeys.Snapshot(basket.BasketId)),
                "Quote-update publish requires the prior Redis snapshot SET to have landed.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Worker_SuppressesNoOpQuoteUpdates_WhenSignalUnchanged()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rig = BuildRig(tempDir);
            var basket = SampleBasket();

            rig.BasketFeed.Enqueue(basket);
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("AAPL", 205m, rig.Clock.UtcNow, previousClose: 200m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("MSFT", 402m, rig.Clock.UtcNow, previousClose: 400m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rig.Worker.StartAsync(cts.Token);

            // First publish: latch signal.
            await WaitForAsync(
                () => rig.QuoteUpdatePublisher.Published.Count >= 1,
                TimeSpan.FromSeconds(5));
            var publishedAfterFirst = rig.QuoteUpdatePublisher.Published.Count;

            // Idle the engine: no new ticks, no clock advance, so the
            // materialize cycle keeps producing the same (nav, qqq, premDisc)
            // tuple with no new series point. Many cycles, zero new publishes.
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            rig.BasketFeed.Complete();
            rig.TickFeed.Complete();
            await rig.Worker.StopAsync(cts.Token);

            var publishedAfterIdle = rig.QuoteUpdatePublisher.Published.Count;

            // We allow the first publish to land, then expect suppression to
            // hold the count flat through the idle window. Permit at most one
            // additional publish for any cycle that raced the latch.
            Assert.True(
                publishedAfterIdle <= publishedAfterFirst + 1,
                $"Expected no-op suppression to hold publishes flat. " +
                $"first={publishedAfterFirst} after={publishedAfterIdle}");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Worker_QuoteUpdatePayload_RoundTripsToFrontendShape()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rig = BuildRig(tempDir);
            var basket = SampleBasket();

            rig.BasketFeed.Enqueue(basket);
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("AAPL", 205m, rig.Clock.UtcNow, previousClose: 200m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("MSFT", 402m, rig.Clock.UtcNow, previousClose: 400m));
            rig.TickFeed.Enqueue(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rig.Worker.StartAsync(cts.Token);

            await WaitForAsync(
                () => rig.QuoteUpdatePublisher.Published.Count > 0,
                TimeSpan.FromSeconds(5));

            rig.BasketFeed.Complete();
            rig.TickFeed.Complete();
            await rig.Worker.StopAsync(cts.Token);

            // Sanity: the published DTO carries the frontend-required scalars
            // and freshness/feeds blocks (camelCase serialization is locked
            // by HqqqJsonDefaults; tested separately in the publisher).
            var update = rig.QuoteUpdatePublisher.Published.First().Update;
            Assert.Equal("live", update.QuoteState);
            Assert.NotNull(update.Freshness);
            Assert.NotNull(update.Feeds);
            Assert.NotNull(update.Movers);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
