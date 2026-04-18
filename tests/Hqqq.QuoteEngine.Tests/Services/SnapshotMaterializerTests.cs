using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Services;

public class SnapshotMaterializerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private static (QuoteEngineServices svc, FakeSystemClock clock) BuildEngine()
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
        var snapMat = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
        var deltaMat = new QuoteDeltaMaterializer(baskets, runtime, snapMat, clock);
        var engine = new Hqqq.QuoteEngine.Services.QuoteEngine(
            quotes, baskets, runtime, calculator, snapMat, deltaMat);

        return (new QuoteEngineServices(quotes, baskets, runtime, calculator, snapMat, deltaMat, engine), clock);
    }

    private sealed record QuoteEngineServices(
        PerSymbolQuoteStore Quotes,
        BasketStateStore Baskets,
        EngineRuntimeState Runtime,
        IncrementalNavCalculator Calculator,
        SnapshotMaterializer Snap,
        QuoteDeltaMaterializer Delta,
        Hqqq.QuoteEngine.Services.QuoteEngine Engine);

    [Fact]
    public void BuildSnapshot_ReturnsNull_WhenNoBasket()
    {
        var (svc, _) = BuildEngine();
        Assert.Null(svc.Snap.Build());
    }

    [Fact]
    public void BuildSnapshot_ProducesDeterministicScalars_FromSeededTicks()
    {
        var (svc, clock) = BuildEngine();

        // Chosen so rawValue = 600_000 and scale = 0.001 → NAV = 600.
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .WithQqqPreviousClose(495m)
            .WithNavPreviousClose(580m)
            .AddConstituent("AAPL", "Apple Inc.", shares: 1000, referencePrice: 200m, weight: 0.3333m)
            .AddConstituent("MSFT", "Microsoft", shares: 500, referencePrice: 400m, weight: 0.3333m)
            .AddConstituent("NVDA", "NVIDIA", shares: 200, referencePrice: 1000m, weight: 0.3334m)
            .Build();

        svc.Engine.OnBasketActivated(basket);

        svc.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, clock.UtcNow, previousClose: 198m));
        svc.Engine.OnTick(TestBasketBuilder.Tick("MSFT", 400m, clock.UtcNow, previousClose: 395m));
        svc.Engine.OnTick(TestBasketBuilder.Tick("NVDA", 1000m, clock.UtcNow, previousClose: 990m));
        svc.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, clock.UtcNow, previousClose: 495m));

        var snap = svc.Snap.Build();

        Assert.NotNull(snap);
        // rawValue = 1000*200 + 500*400 + 200*1000 = 600_000 → NAV = 600
        Assert.Equal(600m, snap!.Nav);
        Assert.Equal(500m, snap.MarketPrice);
        Assert.Equal(500m, snap.Qqq);
        // (500 - 600) / 600 * 100 = -16.6667
        Assert.Equal(-16.6667m, Math.Round(snap.PremiumDiscountPct, 4));
        // (600 - 580) / 580 * 100 = 3.4483
        Assert.Equal(3.4483m, Math.Round(snap.NavChangePct, 4));
        // (500 - 495) / 495 * 100 = 1.0101
        Assert.Equal(1.0101m, Math.Round(snap.QqqChangePct, 4));
        // 600_000 / 1_000_000_000 = 0.0006
        Assert.Equal(0.0006m, snap.BasketValueB);
        Assert.Equal("live", snap.QuoteState);
        Assert.True(snap.IsLive);
        Assert.False(snap.IsFrozen);
    }

    [Fact]
    public void BuildSnapshot_FieldShape_MatchesFrontendAdapterContract()
    {
        var (svc, clock) = BuildEngine();

        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple Inc.", 100, 170m, 1.0m)
            .Build();

        svc.Engine.OnBasketActivated(basket);
        svc.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 170m, clock.UtcNow, previousClose: 168m));
        svc.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 450m, clock.UtcNow));

        var snap = svc.Snap.Build();

        Assert.NotNull(snap);
        Assert.NotNull(snap!.Freshness);
        Assert.NotNull(snap.Feeds);
        Assert.NotNull(snap.Series);
        Assert.NotNull(snap.Movers);
        // AAPL's previous close is available → at least one mover
        Assert.NotEmpty(snap.Movers);
        var mover = snap.Movers[0];
        Assert.Equal("AAPL", mover.Symbol);
        Assert.Equal("Apple Inc.", mover.Name);
        Assert.Contains(mover.Direction, new[] { "up", "down" });

        Assert.Equal(1, snap.Freshness.SymbolsTotal);
        Assert.True(snap.Feeds.PricingActive);
        Assert.Equal("active", snap.Feeds.BasketState);
    }

    [Fact]
    public void BuildSnapshot_RecordsSeriesPointOnEveryCycleWithinInterval()
    {
        var (svc, clock) = BuildEngine();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple Inc.", 100, 170m, 1.0m)
            .Build();

        svc.Engine.OnBasketActivated(basket);
        svc.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 170m, clock.UtcNow));
        svc.Engine.OnTick(TestBasketBuilder.Tick("QQQ", 450m, clock.UtcNow));

        var snap1 = svc.Snap.Build();
        Assert.Single(snap1!.Series); // first tick recorded immediately

        // A second tick 1s later is inside the 5s interval — no new point.
        clock.Advance(TimeSpan.FromSeconds(1));
        svc.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 171m, clock.UtcNow));
        var snap2 = svc.Snap.Build();
        Assert.Single(snap2!.Series);

        // After 5s elapses, the next tick records a new point.
        clock.Advance(TimeSpan.FromSeconds(5));
        svc.Engine.OnTick(TestBasketBuilder.Tick("AAPL", 172m, clock.UtcNow));
        var snap3 = svc.Snap.Build();
        Assert.Equal(2, snap3!.Series.Count);
    }
}
