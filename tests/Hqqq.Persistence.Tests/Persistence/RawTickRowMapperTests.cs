using Hqqq.Contracts.Events;
using Hqqq.Persistence.Persistence;

namespace Hqqq.Persistence.Tests.Persistence;

public class RawTickRowMapperTests
{
    private static RawTickV1 Sample(
        DateTimeOffset? providerTs = null,
        DateTimeOffset? ingressTs = null) => new()
    {
        Symbol = "NVDA",
        Last = 901.2345m,
        Bid = 900.9m,
        Ask = 901.5m,
        Currency = "USD",
        Provider = "tiingo",
        ProviderTimestamp = providerTs ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        IngressTimestamp = ingressTs ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, 50, TimeSpan.Zero),
        Sequence = 42L,
    };

    [Fact]
    public void Map_AllFieldsFlowThroughUnchanged()
    {
        var evt = Sample();

        var row = RawTickRowMapper.Map(evt);

        Assert.Equal(evt.Symbol, row.Symbol);
        Assert.Equal(evt.ProviderTimestamp, row.ProviderTimestamp);
        Assert.Equal(evt.IngressTimestamp, row.IngressTimestamp);
        Assert.Equal(evt.Last, row.Last);
        Assert.Equal(evt.Bid, row.Bid);
        Assert.Equal(evt.Ask, row.Ask);
        Assert.Equal(evt.Currency, row.Currency);
        Assert.Equal(evt.Provider, row.Provider);
        Assert.Equal(evt.Sequence, row.Sequence);
    }

    [Fact]
    public void Map_NormalizesNonUtcProviderTimestampToUtc()
    {
        var easternMinus4 = new TimeSpan(hours: -4, 0, 0);
        var localized = new DateTimeOffset(2026, 4, 16, 9, 30, 0, easternMinus4);

        var row = RawTickRowMapper.Map(Sample(providerTs: localized));

        Assert.Equal(TimeSpan.Zero, row.ProviderTimestamp.Offset);
        Assert.Equal(localized.UtcDateTime, row.ProviderTimestamp.UtcDateTime);
    }

    [Fact]
    public void Map_NormalizesNonUtcIngressTimestampToUtc()
    {
        var easternMinus4 = new TimeSpan(hours: -4, 0, 0);
        var localized = new DateTimeOffset(2026, 4, 16, 9, 30, 0, 50, easternMinus4);

        var row = RawTickRowMapper.Map(Sample(ingressTs: localized));

        Assert.Equal(TimeSpan.Zero, row.IngressTimestamp.Offset);
        Assert.Equal(localized.UtcDateTime, row.IngressTimestamp.UtcDateTime);
    }

    [Fact]
    public void Map_PreservesNullBidAsk()
    {
        var evt = Sample() with { Bid = null, Ask = null };

        var row = RawTickRowMapper.Map(evt);

        Assert.Null(row.Bid);
        Assert.Null(row.Ask);
    }

    [Fact]
    public void Map_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => RawTickRowMapper.Map(null!));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Map_ThrowsOnEmptySymbol(string symbol)
    {
        var evt = Sample() with { Symbol = symbol };
        Assert.Throws<ArgumentException>(() => RawTickRowMapper.Map(evt));
    }

    [Fact]
    public void Map_ThrowsOnEmptyCurrency()
    {
        var evt = Sample() with { Currency = "" };
        Assert.Throws<ArgumentException>(() => RawTickRowMapper.Map(evt));
    }

    [Fact]
    public void Map_ThrowsOnEmptyProvider()
    {
        var evt = Sample() with { Provider = "" };
        Assert.Throws<ArgumentException>(() => RawTickRowMapper.Map(evt));
    }
}
