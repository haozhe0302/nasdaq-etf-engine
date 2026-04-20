using Hqqq.ReferenceData.Standalone;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Round-trip checks for <see cref="BasketSeedToEventMapper"/>: the
/// emitted <c>BasketActiveStateV1</c> must satisfy the constraints the
/// quote-engine consumer enforces (non-empty constituents + entries,
/// positive scale, fingerprint matching the seed).
/// </summary>
public class BasketSeedToEventMapperTests
{
    [Fact]
    public void ToEvent_PopulatesConstituentsAndPricingBasis()
    {
        var seed = BuildSeed();
        var activatedAt = DateTimeOffset.UtcNow;

        var ev = BasketSeedToEventMapper.ToEvent(seed, activatedAt);

        Assert.Equal(seed.BasketId, ev.BasketId);
        Assert.Equal(seed.Version, ev.Version);
        Assert.Equal(seed.AsOfDate, ev.AsOfDate);
        Assert.Equal(seed.Fingerprint, ev.Fingerprint);
        Assert.Equal(activatedAt, ev.ActivatedAtUtc);
        Assert.Equal(seed.ScaleFactor, ev.ScaleFactor);
        Assert.Equal(seed.NavPreviousClose, ev.NavPreviousClose);
        Assert.Equal(seed.QqqPreviousClose, ev.QqqPreviousClose);

        Assert.Equal(seed.Constituents.Count, ev.Constituents.Count);
        Assert.Equal(seed.Constituents.Count, ev.PricingBasis.Entries.Count);
        Assert.All(ev.Constituents, c => Assert.Equal("seed", c.SharesOrigin));
        Assert.All(ev.PricingBasis.Entries, e => Assert.Equal("seed", e.SharesOrigin));
    }

    [Fact]
    public void ToEvent_PricingBasisFingerprintMatchesSeedFingerprint()
    {
        var seed = BuildSeed();
        var ev = BasketSeedToEventMapper.ToEvent(seed, DateTimeOffset.UtcNow);

        Assert.Equal(seed.Fingerprint, ev.PricingBasis.PricingBasisFingerprint);
    }

    [Fact]
    public void ToEvent_UpperCasesSymbols()
    {
        var seed = BuildSeed(("aapl", 1m, 100m), ("Msft", 1m, 200m));
        var ev = BasketSeedToEventMapper.ToEvent(seed, DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "AAPL", "MSFT" }, ev.Constituents.Select(c => c.Symbol).ToArray());
        Assert.Equal(new[] { "AAPL", "MSFT" }, ev.PricingBasis.Entries.Select(e => e.Symbol).ToArray());
    }

    [Fact]
    public void ToEvent_InferredNotionalEqualsSumOfSharesTimesPrice()
    {
        var seed = BuildSeed(("AAPL", 10m, 100m), ("MSFT", 5m, 200m));
        var ev = BasketSeedToEventMapper.ToEvent(seed, DateTimeOffset.UtcNow);

        Assert.Equal(10 * 100m + 5 * 200m, ev.PricingBasis.InferredTotalNotional);
        Assert.Equal(2, ev.PricingBasis.OfficialSharesCount);
        Assert.Equal(0, ev.PricingBasis.DerivedSharesCount);
    }

    private static BasketSeed BuildSeed(params (string Symbol, decimal Shares, decimal Price)[] overrides)
    {
        var constituents = overrides.Length > 0
            ? overrides.Select(o => new BasketSeedConstituent
            {
                Symbol = o.Symbol,
                Name = $"{o.Symbol} Inc.",
                Sector = "Technology",
                SharesHeld = o.Shares,
                ReferencePrice = o.Price,
                TargetWeight = 0.5m,
            }).ToList()
            : new List<BasketSeedConstituent>
            {
                new() { Symbol = "AAPL", Name = "Apple Inc.", Sector = "Technology", SharesHeld = 100, ReferencePrice = 215.30m, TargetWeight = 0.5m },
                new() { Symbol = "MSFT", Name = "Microsoft Corp.", Sector = "Technology", SharesHeld = 100, ReferencePrice = 432.10m, TargetWeight = 0.5m },
            };

        return new BasketSeed
        {
            BasketId = "HQQQ",
            Version = "v-test",
            AsOfDate = new DateOnly(2026, 4, 15),
            ScaleFactor = 1.0m,
            NavPreviousClose = 540.00m,
            QqqPreviousClose = 480.00m,
            Constituents = constituents,
            Fingerprint = "deadbeef" + new string('0', 56),
            Source = "test://memory",
        };
    }
}
