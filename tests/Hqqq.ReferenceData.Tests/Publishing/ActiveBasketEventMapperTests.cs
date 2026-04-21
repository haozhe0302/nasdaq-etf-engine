using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Tests.TestDoubles;

namespace Hqqq.ReferenceData.Tests.Publishing;

/// <summary>
/// The mapper is the contract between the in-memory active basket and the
/// Kafka event. These tests pin down the invariants downstream consumers
/// rely on: full constituent set, complete pricing basis, and lineage tag
/// propagation.
/// </summary>
public class ActiveBasketEventMapperTests
{
    [Fact]
    public void ToEvent_ProducesFullConstituentPayload()
    {
        var active = BuildActive(count: 101);
        var ev = ActiveBasketEventMapper.ToEvent(active);

        // Fully materialized — NOT a header-only event.
        Assert.Equal(101, ev.ConstituentCount);
        Assert.Equal(101, ev.Constituents.Count);
        Assert.Equal(101, ev.PricingBasis.Entries.Count);
    }

    [Fact]
    public void ToEvent_PropagatesLineageToSharesOrigin()
    {
        var active = BuildActive(count: 3, source: "live:http");
        var ev = ActiveBasketEventMapper.ToEvent(active);

        Assert.Equal("live:http", ev.Source);
        Assert.All(ev.Constituents, c => Assert.Equal("live:http", c.SharesOrigin));
        Assert.All(ev.PricingBasis.Entries, e => Assert.Equal("live:http", e.SharesOrigin));
    }

    [Fact]
    public void ToEvent_UppercasesSymbols()
    {
        var snapshot = SnapshotBuilder.Build(count: 2);
        var lower = snapshot with
        {
            Constituents = snapshot.Constituents
                .Select(c => c with { Symbol = c.Symbol.ToLowerInvariant() })
                .ToArray(),
        };
        var active = new ActiveBasket
        {
            Snapshot = lower,
            Fingerprint = "fp",
            ActivatedAtUtc = DateTimeOffset.UtcNow,
        };

        var ev = ActiveBasketEventMapper.ToEvent(active);

        Assert.All(ev.Constituents, c => Assert.Equal(c.Symbol, c.Symbol.ToUpperInvariant()));
        Assert.All(ev.PricingBasis.Entries, e => Assert.Equal(e.Symbol, e.Symbol.ToUpperInvariant()));
    }

    [Fact]
    public void ToEvent_PopulatesBasketIdentityAndFingerprint()
    {
        var active = BuildActive(count: 5);
        var ev = ActiveBasketEventMapper.ToEvent(active);

        Assert.Equal(active.Snapshot.BasketId, ev.BasketId);
        Assert.Equal(active.Snapshot.Version, ev.Version);
        Assert.Equal(active.Snapshot.AsOfDate, ev.AsOfDate);
        Assert.Equal(active.Fingerprint, ev.Fingerprint);
        Assert.Equal(active.Fingerprint, ev.PricingBasis.PricingBasisFingerprint);
        Assert.Equal(active.ActivatedAtUtc, ev.ActivatedAtUtc);
    }

    [Fact]
    public void ToEvent_InferredTotalNotional_EqualsSumOfReferencePriceTimesShares()
    {
        var active = BuildActive(count: 4);
        var ev = ActiveBasketEventMapper.ToEvent(active);

        var expected = ev.PricingBasis.Entries.Sum(e => e.ReferencePrice * e.Shares);
        Assert.Equal(expected, ev.PricingBasis.InferredTotalNotional);
    }

    [Fact]
    public void ToEvent_ScaleFactorMatchesSnapshot()
    {
        var active = BuildActive(count: 2);
        var ev = ActiveBasketEventMapper.ToEvent(active);
        Assert.Equal(active.Snapshot.ScaleFactor, ev.ScaleFactor);
        Assert.True(ev.ScaleFactor > 0);
    }

    private static ActiveBasket BuildActive(int count, string source = "fallback-seed")
        => new()
        {
            Snapshot = SnapshotBuilder.Build(count: count, source: source),
            Fingerprint = $"fp-{count}-{source}",
            ActivatedAtUtc = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
        };
}
