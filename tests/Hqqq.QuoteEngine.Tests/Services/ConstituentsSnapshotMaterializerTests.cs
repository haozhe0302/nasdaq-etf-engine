using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Services;

public class ConstituentsSnapshotMaterializerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private sealed record Harness(
        FakeSystemClock Clock,
        PerSymbolQuoteStore Quotes,
        BasketStateStore Baskets,
        ConstituentsSnapshotMaterializer Materializer);

    private static Harness Build()
    {
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions
        {
            StaleAfter = TimeSpan.FromSeconds(30),
        };
        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var mat = new ConstituentsSnapshotMaterializer(quotes, baskets, clock, options);
        return new Harness(clock, quotes, baskets, mat);
    }

    [Fact]
    public void Build_ReturnsNull_WhenNoBasket()
    {
        var h = Build();
        Assert.Null(h.Materializer.Build());
    }

    [Fact]
    public void Build_ProducesHoldingsAlignedWithFrontendShape()
    {
        var h = Build();
        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-cs-1")
            .WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", shares: 1000, referencePrice: 200m, weight: 0.6m, sector: "Tech")
            .AddConstituent("MSFT", "Microsoft", shares: 500, referencePrice: 400m, weight: 0.4m, sector: "Tech")
            .Build();

        h.Baskets.Replace(basket);
        h.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, h.Clock.UtcNow, previousClose: 198m));
        h.Quotes.Update(TestBasketBuilder.Tick("MSFT", 400m, h.Clock.UtcNow, previousClose: 395m));

        var dto = h.Materializer.Build();

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Holdings.Count);

        var aapl = dto.Holdings.Single(r => r.Symbol == "AAPL");
        Assert.Equal("Apple", aapl.Name);
        Assert.Equal("Tech", aapl.Sector);
        Assert.Equal(60m, aapl.Weight); // 0.6 → 60%
        Assert.Equal(1000m, aapl.Shares);
        Assert.Equal(200m, aapl.Price);
        Assert.Equal(1.0101m, aapl.ChangePct);
        Assert.Equal(200_000m, aapl.MarketValue);
        Assert.Equal("official", aapl.SharesOrigin);
        Assert.False(aapl.IsStale);

        Assert.Equal("fp-cs-1", dto.Source.Fingerprint);
        Assert.Equal("2026-04-16", dto.Source.AsOfDate);
        Assert.Equal("official", dto.Source.BasketMode);

        Assert.Equal(2, dto.Quality.TotalSymbols);
        Assert.Equal(2, dto.Quality.PricedCount);
        Assert.Equal(0, dto.Quality.StaleCount);
        Assert.Equal(100m, dto.Quality.PriceCoveragePct);
        Assert.Equal("official", dto.Quality.BasketMode);

        Assert.Equal(100m, dto.Concentration.Top5Pct); // only two constituents
        Assert.Equal(1, dto.Concentration.SectorCount);
    }

    [Fact]
    public void Build_PropagatesAnchoredMergeProvenance_ToSourceAndQuality()
    {
        var h = Build();
        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-anchored")
            .WithProvenance(
                anchorSource: "stockanalysis",
                tailSource: "alphavantage",
                basketMode: "anchored",
                isDegraded: false)
            .AddConstituent("AAPL", "Apple", shares: 1000, referencePrice: 200m, weight: 0.6m, sector: "Tech", sharesOrigin: "stockanalysis")
            .AddConstituent("MSFT", "Microsoft", shares: 500, referencePrice: 400m, weight: 0.4m, sector: "Tech", sharesOrigin: "schwab")
            .Build();

        h.Baskets.Replace(basket);

        var dto = h.Materializer.Build();
        Assert.NotNull(dto);

        Assert.Equal("stockanalysis", dto!.Source.AnchorSource);
        Assert.Equal("alphavantage", dto.Source.TailSource);
        Assert.Equal("anchored", dto.Source.BasketMode);
        Assert.False(dto.Source.IsDegraded);

        Assert.Equal("anchored", dto.Quality.BasketMode);
    }

    [Fact]
    public void Build_LegacyBasketWithoutProvenance_FallsBackToOfficialOrHybrid()
    {
        var h = Build();
        var basket = new TestBasketBuilder()
            .AddConstituent("AAPL", "Apple", shares: 1000, referencePrice: 200m, weight: 1.0m, sector: "Tech")
            .Build();
        h.Baskets.Replace(basket);

        var dto = h.Materializer.Build();
        Assert.NotNull(dto);

        Assert.Equal(string.Empty, dto!.Source.AnchorSource);
        Assert.Equal(string.Empty, dto.Source.TailSource);
        Assert.Equal("official", dto.Source.BasketMode);
        Assert.False(dto.Source.IsDegraded);
    }

    [Fact]
    public void Build_MarksSymbolsWithoutTicksAsStaleAndLeavesPriceNull()
    {
        var h = Build();
        var basket = new TestBasketBuilder()
            .AddConstituent("AAPL", "Apple", 100, 170m, 1.0m)
            .Build();
        h.Baskets.Replace(basket);

        var dto = h.Materializer.Build();

        Assert.NotNull(dto);
        var row = Assert.Single(dto!.Holdings);
        Assert.Null(row.Price);
        Assert.Null(row.ChangePct);
        Assert.Null(row.MarketValue);
        Assert.True(row.IsStale);
        Assert.Equal(0m, dto.Quality.PriceCoveragePct);
        Assert.Equal(1, dto.Quality.StaleCount);
    }
}
