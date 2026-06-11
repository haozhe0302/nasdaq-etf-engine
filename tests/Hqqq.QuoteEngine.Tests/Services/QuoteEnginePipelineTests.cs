using Hqqq.Domain.Services;
using Hqqq.Domain.ValueObjects;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Services;

public class QuoteEnginePipelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private sealed record Rig(
        Hqqq.QuoteEngine.Services.QuoteEngine Engine,
        FakeSystemClock Clock,
        QuoteEngineOptions Options,
        EngineRuntimeState Runtime);

    private static Rig BuildRig()
    {
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions
        {
            StaleAfter = TimeSpan.FromSeconds(30),
            SeriesRecordInterval = TimeSpan.FromSeconds(5),
            AnchorSymbol = "QQQ",
            SeriesCapacity = 64,
            MoversTopN = 5,
        };
        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var runtime = new EngineRuntimeState(options.SeriesCapacity);
        var calculator = new IncrementalNavCalculator(quotes, baskets, runtime, clock, options);
        var snap = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
        var delta = new QuoteDeltaMaterializer(baskets, runtime, snap, clock);
        var engine = new Hqqq.QuoteEngine.Services.QuoteEngine(
            quotes, baskets, runtime, calculator, snap, delta,
            NullBootstrapCalibrationCoordinator.Instance);
        return new Rig(engine, clock, options, runtime);
    }

    [Fact]
    public void Pipeline_Activate_ThenTicks_ProducesLiveSnapshotAndDelta()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .WithNavPreviousClose(550m)
            .WithQqqPreviousClose(495m)
            .AddConstituent("AAPL", "Apple",    1000, 200m, 0.333m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.333m)
            .AddConstituent("NVDA", "NVIDIA",    200, 1000m, 0.334m)
            .Build();

        rig.Engine.OnBasketActivated(basket);

        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 205m, rig.Clock.UtcNow, previousClose: 200m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("MSFT", 402m, rig.Clock.UtcNow, previousClose: 400m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("NVDA", 1010m, rig.Clock.UtcNow, previousClose: 1000m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

        var snap = rig.Engine.BuildSnapshot();
        var delta = rig.Engine.BuildDelta();

        Assert.NotNull(snap);
        Assert.NotNull(delta);
        Assert.Equal("live", snap!.QuoteState);
        Assert.True(snap.IsLive);
        Assert.False(snap.IsFrozen);

        // rawValue = 205*1000 + 402*500 + 1010*200 = 205000 + 201000 + 202000 = 608_000
        // nav = 0.001 * 608_000 = 608
        Assert.Equal(608m, snap.Nav);
        Assert.Equal(500m, snap.MarketPrice);

        // Delta scalars match snapshot scalars.
        Assert.Equal(snap.Nav, delta!.Nav);
        Assert.Equal(snap.Qqq, delta.Qqq);
    }

    [Fact]
    public void Pipeline_AllStale_FlipsQuoteStateToFrozenAllStale()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple",    1000, 200m, 0.5m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.5m)
            .Build();

        rig.Engine.OnBasketActivated(basket);

        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Engine.OnTick(TestBasketBuilder.Tick("MSFT", 400m, rig.Clock.UtcNow));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        // Skip past the stale threshold and re-run the calculator via
        // any additional tick. Use a fresh QQQ tick so the anchor stays
        // live; we only want basket symbols to age out.
        rig.Clock.Advance(TimeSpan.FromSeconds(120));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        var snap = rig.Engine.BuildSnapshot();
        var delta = rig.Engine.BuildDelta();

        Assert.NotNull(snap);
        Assert.Equal("frozen_all_stale", snap!.QuoteState);
        Assert.False(snap.IsLive);
        Assert.True(snap.IsFrozen);
        Assert.Equal("All tracked symbols are stale", snap.PauseReason);
        Assert.Equal("frozen_all_stale", delta!.QuoteState);
        Assert.True(delta.IsFrozen);
    }

    [Fact]
    public void Pipeline_UnsupportedWithoutBasket_IsUninitialized()
    {
        var rig = BuildRig();

        Assert.False(rig.Engine.IsInitialized);
        Assert.Null(rig.Engine.BuildSnapshot());
        Assert.Null(rig.Engine.BuildDelta());

        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));

        Assert.False(rig.Engine.IsInitialized);
        Assert.Null(rig.Engine.BuildSnapshot());
    }

    [Fact]
    public void Pipeline_BasketReactivation_ReplacesBasis()
    {
        var rig = BuildRig();

        var first = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .WithFingerprint("fp-1")
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        var second = new TestBasketBuilder()
            .WithScaleFactor(0.0005m)
            .WithFingerprint("fp-2")
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 1.0m)
            .Build();

        rig.Engine.OnBasketActivated(first);
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        var firstSnap = rig.Engine.BuildSnapshot();
        Assert.Equal(200m, firstSnap!.Nav); // 0.001 * (200 * 1000) = 200

        rig.Engine.OnBasketActivated(second);
        rig.Engine.OnTick(TestBasketBuilder.Tick("MSFT", 400m, rig.Clock.UtcNow));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        var secondSnap = rig.Engine.BuildSnapshot();
        Assert.Equal(100m, secondSnap!.Nav); // 0.0005 * (400 * 500) = 100
        Assert.Equal(1, secondSnap.Freshness.SymbolsTotal); // only MSFT tracked now
    }

    [Fact]
    public void Pipeline_WithBootstrapCoordinator_AnchorTickProducesRealisticNav()
    {
        // End-to-end coverage of the long-term hardening Issue 2: a
        // freshly activated basket with placeholder ScaleFactor=1
        // routed through the coordinator must yield nav ≈ QQQ price
        // (anchor) on the first complete tick set.
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions { AnchorSymbol = "QQQ", SeriesCapacity = 64 };
        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var runtime = new EngineRuntimeState(options.SeriesCapacity);
        var calculator = new IncrementalNavCalculator(quotes, baskets, runtime, clock, options);
        var snap = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
        var delta = new QuoteDeltaMaterializer(baskets, runtime, snap, clock);
        var calibrationStore = new InMemoryCalibrationStore();
        var coordinator = new BootstrapCalibrationCoordinator(
            baskets, quotes, options, calibrationStore, clock,
            NullLogger<BootstrapCalibrationCoordinator>.Instance);

        var engine = new Hqqq.QuoteEngine.Services.QuoteEngine(
            quotes, baskets, runtime, calculator, snap, delta, coordinator);

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)             // reference-data placeholder
            .AddConstituent("AAPL", "Apple",     1000, 200m,  0.333m)
            .AddConstituent("MSFT", "Microsoft",  500, 400m,  0.333m)
            .AddConstituent("NVDA", "NVIDIA",     200, 1000m, 0.334m)
            .Build();

        engine.OnBasketActivated(basket);

        // Before the first complete tick set: coordinator has downgraded
        // scale to Uninitialized; the materializer flags the snapshot as
        // not-yet-live so /api/quote stays off-air semantically (NAV=0).
        Assert.False(engine.IsInitialized);
        var preBootstrap = engine.BuildSnapshot();
        Assert.NotNull(preBootstrap);
        Assert.Equal("uninitialized", preBootstrap!.QuoteState);
        Assert.False(preBootstrap.IsLive);
        Assert.Equal(0m, preBootstrap.Nav);

        engine.OnTick(TestBasketBuilder.Tick("AAPL", 205m, clock.UtcNow, previousClose: 200m));
        engine.OnTick(TestBasketBuilder.Tick("MSFT", 402m, clock.UtcNow, previousClose: 400m));
        engine.OnTick(TestBasketBuilder.Tick("NVDA", 1010m, clock.UtcNow, previousClose: 1000m));
        engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, clock.UtcNow, previousClose: 495m));

        var snapshot = engine.BuildSnapshot();
        Assert.NotNull(snapshot);
        // rawValue = 205*1000 + 402*500 + 1010*200 = 608_000
        // scale ≈ 500/608000 → nav ≈ 500
        Assert.Equal(500m, Math.Round(snapshot!.Nav, 0));
        Assert.InRange(snapshot.Nav, 100m, 1000m);
        Assert.Equal(500m, snapshot.MarketPrice);
        Assert.Equal(500m, snapshot.Qqq);
        Assert.Equal(1, calibrationStore.WriteCount);
    }

    [Fact]
    public void Pipeline_WithBootstrapCoordinator_RestartRestoresCalibration()
    {
        // Round-trip: calibrate via the coordinator, write to the store,
        // then simulate a process restart by creating a fresh engine
        // with the same store. The new engine must restore scale on
        // activation (no QQQ tick required) so /api/quote.nav is live
        // from t=0 instead of waiting for the first anchor tick.
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions { AnchorSymbol = "QQQ", SeriesCapacity = 64 };
        var sharedStore = new InMemoryCalibrationStore();

        Hqqq.QuoteEngine.Services.QuoteEngine BuildEngine(
            out BasketStateStore baskets,
            out PerSymbolQuoteStore quotes)
        {
            quotes = new PerSymbolQuoteStore(clock);
            baskets = new BasketStateStore();
            var runtime = new EngineRuntimeState(options.SeriesCapacity);
            var calculator = new IncrementalNavCalculator(quotes, baskets, runtime, clock, options);
            var snap = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
            var delta = new QuoteDeltaMaterializer(baskets, runtime, snap, clock);
            var coordinator = new BootstrapCalibrationCoordinator(
                baskets, quotes, options, sharedStore, clock,
                NullLogger<BootstrapCalibrationCoordinator>.Instance);
            return new Hqqq.QuoteEngine.Services.QuoteEngine(
                quotes, baskets, runtime, calculator, snap, delta, coordinator);
        }

        // ── First lifetime: calibrate from scratch.
        var firstEngine = BuildEngine(out var firstBaskets, out var firstQuotes);
        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-survive")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        firstEngine.OnBasketActivated(basket);
        firstEngine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, clock.UtcNow));
        firstEngine.OnTick(TestBasketBuilder.Tick("QQQ", 480m, clock.UtcNow));
        var calibratedScale = firstBaskets.Current!.ScaleFactor.Value;
        Assert.True(calibratedScale > 0m);
        Assert.NotNull(sharedStore.Peek("HQQQ"));

        // ── Restart: fresh engine with the same persistent store.
        var secondEngine = BuildEngine(out var secondBaskets, out var _);
        secondEngine.OnBasketActivated(basket);

        Assert.True(secondBaskets.Current!.ScaleFactor.IsInitialized);
        Assert.Equal(calibratedScale, secondBaskets.Current.ScaleFactor.Value);
    }

    [Fact]
    public void Pipeline_UsesMigratedScaleFactorCalibratorForContinuity()
    {
        // Smoke-check that the domain calibrator is wired up correctly and
        // keeps NAV continuous across a basis swap when the caller uses it
        // to pick the new scale.
        var oldScale = new ScaleFactor(0.001m);
        var oldRaw = 600_000m;
        var newRaw = 550_000m;

        var newScale = ScaleFactorCalibrator.RecalibrateForContinuity(oldScale, oldRaw, newRaw);

        Assert.True(newScale.IsInitialized);
        Assert.Equal(
            Math.Round(oldScale.Value * oldRaw, 6),
            Math.Round(newScale.Value * newRaw, 6));
    }

    [Fact]
    public void Pipeline_DoesNotRecordSeriesOutsideRegularSession()
    {
        // 2026-04-16 20:10Z == 16:10 ET (post-close).
        var rig = BuildRig();
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 16, 20, 10, 0, TimeSpan.Zero));

        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        rig.Engine.OnBasketActivated(basket);
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 201m, rig.Clock.UtcNow, previousClose: 200m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));

        var snapshot = rig.Engine.BuildSnapshot();
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Series);
    }

    [Fact]
    public void Pipeline_PreOpenReset_ClearsAt0925AndNextRegularSessionRebuilds()
    {
        var rig = BuildRig();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        rig.Engine.OnBasketActivated(basket);

        // Day 1 regular session point (2026-04-16 19:50Z == 15:50 ET).
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 16, 19, 50, 0, TimeSpan.Zero));
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 201m, rig.Clock.UtcNow, previousClose: 200m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow, previousClose: 495m));
        var day1 = rig.Engine.BuildSnapshot();
        Assert.NotNull(day1);
        Assert.NotEmpty(day1!.Series);
        var day1Count = day1.Series.Count;

        // Day 2 pre-open but before reset window (09:10 ET) keeps Day 1 series.
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 17, 13, 10, 0, TimeSpan.Zero));
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 202m, rig.Clock.UtcNow, previousClose: 200m));
        var beforeReset = rig.Engine.BuildSnapshot();
        Assert.Equal(day1Count, beforeReset!.Series.Count);

        // Day 2 pre-open reset window start (09:25 ET): clear once and keep empty.
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 17, 13, 25, 0, TimeSpan.Zero));
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 203m, rig.Clock.UtcNow, previousClose: 200m));
        var atReset = rig.Engine.BuildSnapshot();
        Assert.Empty(atReset!.Series);

        // During the same pre-open window, a second cycle must not re-clear
        // again once today's reset has fired.
        rig.Runtime.RecordSeriesPoint(new Hqqq.Contracts.Dtos.SeriesPointDto
        {
            Time = rig.Clock.UtcNow,
            Nav = 123.45m,
            Market = 456.78m,
        });
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 17, 13, 26, 0, TimeSpan.Zero));
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 203m, rig.Clock.UtcNow, previousClose: 200m));
        var sameWindow = rig.Engine.BuildSnapshot();
        Assert.Single(sameWindow!.Series);

        // Regular session opens at 09:30 ET: live series recording resumes.
        rig.Clock.SetTo(new DateTimeOffset(2026, 4, 17, 13, 30, 0, TimeSpan.Zero));
        rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 204m, rig.Clock.UtcNow, previousClose: 200m));
        rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 501m, rig.Clock.UtcNow, previousClose: 495m));
        var day2Open = rig.Engine.BuildSnapshot();
        Assert.True(day2Open!.Series.Count >= 2);
    }

    [Fact]
    public void Pipeline_SeriesRetention_DoesNotDropEarlySessionPoints_WhenCountExceedsLegacyCapacity()
    {
        var rig = BuildRig();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        rig.Engine.OnBasketActivated(basket);

        // Start at regular-session open (2026-04-16 13:30Z == 09:30 ET).
        var sessionOpenUtc = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
        rig.Clock.SetTo(sessionOpenUtc);

        // Legacy ring capacity in this rig is 64. Record 80 points and verify
        // the first point is still present (no overwrite-by-cap behavior).
        const int pointsToRecord = 80;
        for (var i = 0; i < pointsToRecord; i++)
        {
            rig.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m + i, rig.Clock.UtcNow, previousClose: 199m));
            rig.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m + (i * 0.01m), rig.Clock.UtcNow, previousClose: 495m));
            rig.Clock.Advance(TimeSpan.FromSeconds(5));
        }

        var snapshot = rig.Engine.BuildSnapshot();
        Assert.NotNull(snapshot);
        Assert.Equal(pointsToRecord, snapshot!.Series.Count);
        Assert.Equal(sessionOpenUtc, snapshot.Series[0].Time);
        Assert.Equal(200m, snapshot.Series[0].Nav);
    }
}
