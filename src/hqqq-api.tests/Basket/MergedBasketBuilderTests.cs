using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.Basket.Services;

namespace Hqqq.Api.Tests.Basket;

public class MergedBasketBuilderTests
{
    private static BasketConstituent MakeConstituent(
        string symbol, decimal weight, decimal shares = 1000m) =>
        new()
        {
            Symbol = symbol,
            SecurityName = symbol,
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = weight,
            SharesHeld = shares,
            AsOfDate = new DateOnly(2026, 3, 27),
        };

    private static MergedBasketBuilder.TailEntry MakeTail(
        string symbol, decimal rawWeight) =>
        new(symbol, symbol, rawWeight, "Technology");

    [Fact]
    public void Build_NormalizesAnchorAndTailWeightsToSumToOne()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [
                MakeConstituent("AAPL", 0.30m),
                MakeConstituent("MSFT", 0.25m),
            ],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [
                MakeTail("GOOG", 0.5m),
                MakeTail("AMZN", 0.3m),
                MakeTail("META", 0.2m),
            ],
            "alphavantage",
            IsProxy: false);

        var result = MergedBasketBuilder.Build(anchor, tail);

        Assert.Equal(5, result.Constituents.Count);

        var totalWeight = result.Constituents.Sum(c => c.Weight ?? 0m);
        Assert.InRange(totalWeight, 0.999m, 1.001m);
    }

    [Fact]
    public void Build_RemovesAnchorOverlapFromTail()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [MakeConstituent("AAPL", 0.50m)],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [
                MakeTail("AAPL", 0.3m),
                MakeTail("GOOG", 0.7m),
            ],
            "alphavantage",
            IsProxy: false);

        var result = MergedBasketBuilder.Build(anchor, tail);

        Assert.Equal(2, result.Constituents.Count);
        Assert.Single(result.Constituents, c => c.Symbol == "AAPL");
        Assert.Single(result.Constituents, c => c.Symbol == "GOOG");
    }

    [Fact]
    public void Build_DropsDirtySymbols()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [MakeConstituent("AAPL", 0.80m)],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [
                MakeTail("", 0.1m),
                MakeTail("  ", 0.1m),
                MakeTail("GOOG", 0.8m),
            ],
            "alphavantage",
            IsProxy: false);

        var result = MergedBasketBuilder.Build(anchor, tail);

        Assert.Equal(2, result.Constituents.Count);
        Assert.DoesNotContain(result.Constituents, c => string.IsNullOrWhiteSpace(c.Symbol));
    }

    [Fact]
    public void Build_DropsSymbolsOutsideUniverse_WhenProvided()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [MakeConstituent("AAPL", 0.50m)],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [
                MakeTail("GOOG", 0.5m),
                MakeTail("XYZ_NOT_IN_UNIVERSE", 0.5m),
            ],
            "alphavantage",
            IsProxy: false);

        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "GOOG" };
        var result = MergedBasketBuilder.Build(anchor, tail, universe);

        Assert.Equal(2, result.Constituents.Count);
        Assert.DoesNotContain(result.Constituents, c => c.Symbol == "XYZ_NOT_IN_UNIVERSE");
        Assert.Equal(1, result.QualityReport.UniverseDroppedCount);
    }

    [Fact]
    public void Build_QualityReport_ReflectsBasketMode()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [MakeConstituent("AAPL", 0.50m)],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [MakeTail("GOOG", 0.5m)],
            "alphavantage",
            IsProxy: false);

        var result = MergedBasketBuilder.Build(anchor, tail);
        Assert.Equal("hybrid", result.QualityReport.BasketMode);

        var proxyTail = new MergedBasketBuilder.TailBlock(
            [MakeTail("GOOG", 0.5m)],
            "nasdaq-proxy",
            IsProxy: true);

        var degraded = MergedBasketBuilder.Build(anchor, proxyTail);
        Assert.Equal("degraded", degraded.QualityReport.BasketMode);
    }

    [Fact]
    public void Build_HandlesDeduplicate_InTailBlock()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            [MakeConstituent("AAPL", 0.60m)],
            "stockanalysis",
            new DateOnly(2026, 3, 27));

        var tail = new MergedBasketBuilder.TailBlock(
            [
                MakeTail("GOOG", 0.3m),
                MakeTail("GOOG", 0.4m),
            ],
            "alphavantage",
            IsProxy: false);

        var result = MergedBasketBuilder.Build(anchor, tail);

        var googCount = result.Constituents.Count(c => c.Symbol == "GOOG");
        Assert.Equal(1, googCount);
    }

    [Fact]
    public void ComputeFingerprint_IsDeterministic()
    {
        var constituents = new List<BasketConstituent>
        {
            MakeConstituent("MSFT", 0.25m),
            MakeConstituent("AAPL", 0.30m),
        };
        var date = new DateOnly(2026, 3, 27);

        var fp1 = MergedBasketBuilder.ComputeFingerprint(constituents, date);
        var fp2 = MergedBasketBuilder.ComputeFingerprint(constituents, date);

        Assert.Equal(fp1, fp2);
        Assert.Equal(16, fp1.Length);
    }

    [Fact]
    public void ComputeFingerprint_IsOrderIndependent()
    {
        var date = new DateOnly(2026, 3, 27);

        var list1 = new List<BasketConstituent>
        {
            MakeConstituent("AAPL", 0.30m),
            MakeConstituent("MSFT", 0.25m),
        };
        var list2 = new List<BasketConstituent>
        {
            MakeConstituent("MSFT", 0.25m),
            MakeConstituent("AAPL", 0.30m),
        };

        Assert.Equal(
            MergedBasketBuilder.ComputeFingerprint(list1, date),
            MergedBasketBuilder.ComputeFingerprint(list2, date));
    }

    [Fact]
    public void ComputeFingerprint_DiffersForDifferentDates()
    {
        var constituents = new List<BasketConstituent>
        {
            MakeConstituent("AAPL", 0.30m),
        };

        var fp1 = MergedBasketBuilder.ComputeFingerprint(constituents, new DateOnly(2026, 3, 27));
        var fp2 = MergedBasketBuilder.ComputeFingerprint(constituents, new DateOnly(2026, 3, 28));

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_DiffersForDifferentWeights()
    {
        var date = new DateOnly(2026, 3, 27);

        var list1 = new List<BasketConstituent> { MakeConstituent("AAPL", 0.30m) };
        var list2 = new List<BasketConstituent> { MakeConstituent("AAPL", 0.31m) };

        Assert.NotEqual(
            MergedBasketBuilder.ComputeFingerprint(list1, date),
            MergedBasketBuilder.ComputeFingerprint(list2, date));
    }
}
