using Hqqq.Domain.Services;
using Hqqq.Domain.ValueObjects;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Services;

public class QuoteEnginePipelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private sealed record Rig(
        Hqqq.QuoteEngine.Services.QuoteEngine Engine,
        FakeSystemClock Clock,
        QuoteEngineOptions Options);

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
            quotes, baskets, runtime, calculator, snap, delta);
        return new Rig(engine, clock, options);
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
}
