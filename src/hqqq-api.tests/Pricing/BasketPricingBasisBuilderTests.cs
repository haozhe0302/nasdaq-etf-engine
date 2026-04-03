using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.Pricing.Services;

namespace Hqqq.Api.Tests.Pricing;

public class BasketPricingBasisBuilderTests
{
    private readonly BasketPricingBasisBuilder _builder = new();

    private static BasketSnapshot MakeBasket(params BasketConstituent[] constituents) =>
        new()
        {
            AsOfDate = new DateOnly(2026, 3, 27),
            Constituents = constituents.ToList(),
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Source = new BasketSourceInfo
            {
                SourceName = "test",
                SourceType = "test",
                IsDegraded = false,
                SourceAsOfDate = new DateOnly(2026, 3, 27),
                FetchedAtUtc = DateTimeOffset.UtcNow,
                CacheWrittenAtUtc = DateTimeOffset.UtcNow,
                OfficialWeightsAvailable = true,
                OfficialSharesAvailable = true,
            },
            Fingerprint = "test-fp",
        };

    private static BasketConstituent OfficialConstituent(
        string symbol, decimal weight, decimal shares) =>
        new()
        {
            Symbol = symbol,
            SecurityName = symbol,
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = weight,
            SharesHeld = shares,
            AsOfDate = new DateOnly(2026, 3, 27),
            SharesSource = "official",
        };

    private static BasketConstituent DerivedConstituent(
        string symbol, decimal weight) =>
        new()
        {
            Symbol = symbol,
            SecurityName = symbol,
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = weight,
            SharesHeld = 0m,
            AsOfDate = new DateOnly(2026, 3, 27),
            SharesSource = "unavailable",
        };

    [Fact]
    public void Build_SplitsOfficialAndDerivedEntries()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.30m, 45000),
            DerivedConstituent("GOOG", 0.10m));

        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 200m,
            ["GOOG"] = 150m,
        };

        var basis = _builder.Build(basket, prices);

        Assert.Equal(1, basis.OfficialSharesCount);
        Assert.Equal(1, basis.DerivedSharesCount);
        Assert.Equal(2, basis.Entries.Count);

        var official = basis.Entries.Single(e => e.Symbol == "AAPL");
        Assert.Equal("official", official.SharesOrigin);
        Assert.Equal(45000, official.Shares);

        var derived = basis.Entries.Single(e => e.Symbol == "GOOG");
        Assert.Equal("derived", derived.SharesOrigin);
        Assert.True(derived.Shares > 0);
    }

    [Fact]
    public void Build_DerivedShares_AreProportionalToWeight()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.50m, 50000),
            DerivedConstituent("GOOG", 0.20m),
            DerivedConstituent("AMZN", 0.10m));

        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 200m,
            ["GOOG"] = 100m,
            ["AMZN"] = 100m,
        };

        var basis = _builder.Build(basket, prices);

        var goog = basis.Entries.Single(e => e.Symbol == "GOOG");
        var amzn = basis.Entries.Single(e => e.Symbol == "AMZN");

        Assert.True(goog.Shares > amzn.Shares,
            "GOOG (20% weight) should have more shares than AMZN (10% weight) at same price");
    }

    [Fact]
    public void Build_SkipsEntries_WithNoPrice()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.50m, 50000),
            OfficialConstituent("MISSING", 0.10m, 10000));

        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 200m,
        };

        var basis = _builder.Build(basket, prices);

        Assert.Single(basis.Entries);
        Assert.Equal("AAPL", basis.Entries[0].Symbol);
    }

    [Fact]
    public void Build_SkipsEntries_WithZeroPrice()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.50m, 50000));

        var prices = new Dictionary<string, decimal> { ["AAPL"] = 0m };

        var basis = _builder.Build(basket, prices);
        Assert.Empty(basis.Entries);
    }

    [Fact]
    public void Build_PricingBasisFingerprint_IsDeterministic()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.50m, 50000),
            OfficialConstituent("MSFT", 0.30m, 30000));

        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 200m,
            ["MSFT"] = 400m,
        };

        var fp1 = _builder.Build(basket, prices).PricingBasisFingerprint;
        var fp2 = _builder.Build(basket, prices).PricingBasisFingerprint;

        Assert.Equal(fp1, fp2);
        Assert.Equal(16, fp1.Length);
    }

    [Fact]
    public void Build_UsesDefaultNotional_WhenNoOfficialShares()
    {
        var basket = MakeBasket(
            DerivedConstituent("GOOG", 0.50m),
            DerivedConstituent("AMZN", 0.50m));

        var prices = new Dictionary<string, decimal>
        {
            ["GOOG"] = 150m,
            ["AMZN"] = 200m,
        };

        var basis = _builder.Build(basket, prices);

        Assert.Equal(0, basis.OfficialSharesCount);
        Assert.Equal(2, basis.DerivedSharesCount);
        Assert.True(basis.InferredTotalNotional > 0);
    }

    [Fact]
    public void Build_InfersTotalNotional_FromAnchorBlock()
    {
        var basket = MakeBasket(
            OfficialConstituent("AAPL", 0.50m, 50000),
            DerivedConstituent("GOOG", 0.25m));

        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 200m,
            ["GOOG"] = 150m,
        };

        var basis = _builder.Build(basket, prices);

        var expectedNotional = 200m * 50000 / 0.50m;
        Assert.Equal(expectedNotional, basis.InferredTotalNotional);
    }
}
