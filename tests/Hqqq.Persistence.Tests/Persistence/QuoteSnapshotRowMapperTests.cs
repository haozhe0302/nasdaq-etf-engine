using Hqqq.Contracts.Events;
using Hqqq.Persistence.Persistence;

namespace Hqqq.Persistence.Tests.Persistence;

public class QuoteSnapshotRowMapperTests
{
    private static QuoteSnapshotV1 Sample(DateTimeOffset? ts = null) => new()
    {
        BasketId = "HQQQ",
        Timestamp = ts ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        Nav = 601.2345m,
        MarketProxyPrice = 499.8765m,
        PremiumDiscountPct = -16.6667m,
        StaleCount = 2,
        FreshCount = 48,
        MaxComponentAgeMs = 123.45d,
        QuoteQuality = "stale",
    };

    [Fact]
    public void Map_AllFieldsFlowThroughUnchanged()
    {
        var evt = Sample();

        var row = QuoteSnapshotRowMapper.Map(evt);

        Assert.Equal(evt.BasketId, row.BasketId);
        Assert.Equal(evt.Timestamp, row.Ts);
        Assert.Equal(evt.Nav, row.Nav);
        Assert.Equal(evt.MarketProxyPrice, row.MarketProxyPrice);
        Assert.Equal(evt.PremiumDiscountPct, row.PremiumDiscountPct);
        Assert.Equal(evt.StaleCount, row.StaleCount);
        Assert.Equal(evt.FreshCount, row.FreshCount);
        Assert.Equal(evt.MaxComponentAgeMs, row.MaxComponentAgeMs);
        Assert.Equal(evt.QuoteQuality, row.QuoteQuality);
    }

    [Fact]
    public void Map_NormalizesNonUtcTimestampToUtc()
    {
        var easternPlus4 = new TimeSpan(hours: -4, 0, 0);
        var localized = new DateTimeOffset(2026, 4, 16, 9, 30, 0, easternPlus4);

        var row = QuoteSnapshotRowMapper.Map(Sample(localized));

        Assert.Equal(TimeSpan.Zero, row.Ts.Offset);
        Assert.Equal(localized.UtcDateTime, row.Ts.UtcDateTime);
    }

    [Fact]
    public void Map_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => QuoteSnapshotRowMapper.Map(null!));
    }

    [Fact]
    public void Map_ThrowsOnEmptyBasketId()
    {
        var evt = Sample() with { BasketId = "   " };
        Assert.Throws<ArgumentException>(() => QuoteSnapshotRowMapper.Map(evt));
    }
}
