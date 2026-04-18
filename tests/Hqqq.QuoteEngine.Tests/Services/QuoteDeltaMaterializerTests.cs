using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Services;

public class QuoteDeltaMaterializerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private static (Hqqq.QuoteEngine.Services.QuoteEngine engine,
                    SnapshotMaterializer snap,
                    QuoteDeltaMaterializer delta,
                    FakeSystemClock clock)
        BuildEngine()
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

        return (engine, snap, delta, clock);
    }

    [Fact]
    public void BuildDelta_ReturnsNull_WhenNoBasket()
    {
        var (_, _, delta, _) = BuildEngine();
        Assert.Null(delta.Build());
    }

    [Fact]
    public void BuildDelta_CarriesScalarsAndLatestSeriesPoint_OnFreshCycle()
    {
        var (engine, _, delta, clock) = BuildEngine();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        engine.OnBasketActivated(basket);
        engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, clock.UtcNow));
        engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, clock.UtcNow));

        var d = delta.Build();

        Assert.NotNull(d);
        Assert.Equal(200m, d!.Nav);
        Assert.Equal(500m, d.MarketPrice);
        Assert.NotNull(d.LatestSeriesPoint);
        Assert.Equal(clock.UtcNow, d.LatestSeriesPoint!.Time);
    }

    [Fact]
    public void BuildDelta_SuppressesLatestSeriesPoint_OnSecondCycle()
    {
        var (engine, _, delta, clock) = BuildEngine();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        engine.OnBasketActivated(basket);
        engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, clock.UtcNow));
        engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, clock.UtcNow));

        // First delta consumes the recorded point.
        var first = delta.Build();
        Assert.NotNull(first!.LatestSeriesPoint);

        // Without advancing past the record interval, second delta must have null.
        var second = delta.Build();
        Assert.Null(second!.LatestSeriesPoint);
    }

    [Fact]
    public void BuildDelta_ScalarsMirrorSnapshot()
    {
        var (engine, snap, delta, clock) = BuildEngine();
        var basket = new TestBasketBuilder()
            .WithScaleFactor(0.001m)
            .WithQqqPreviousClose(495m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        engine.OnBasketActivated(basket);
        engine.OnTick(TestBasketBuilder.Tick("AAPL", 200m, clock.UtcNow, previousClose: 198m));
        engine.OnTick(TestBasketBuilder.Tick("QQQ", 500m, clock.UtcNow, previousClose: 495m));

        var s = snap.Build();
        var d = delta.Build();

        Assert.NotNull(s);
        Assert.NotNull(d);
        Assert.Equal(s!.Nav, d!.Nav);
        Assert.Equal(s.MarketPrice, d.MarketPrice);
        Assert.Equal(s.Qqq, d.Qqq);
        Assert.Equal(s.QqqChangePct, d.QqqChangePct);
        Assert.Equal(s.PremiumDiscountPct, d.PremiumDiscountPct);
        Assert.Equal(s.Movers.Count, d.Movers.Count);
        Assert.Equal(s.Freshness.SymbolsTotal, d.Freshness.SymbolsTotal);
        Assert.Equal(s.QuoteState, d.QuoteState);
    }
}
