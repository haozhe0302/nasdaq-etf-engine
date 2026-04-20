using Hqqq.Contracts.Events;
using Hqqq.Ingress.Normalization;

namespace Hqqq.Ingress.Tests;

public class TiingoQuoteNormalizerTests
{
    [Fact]
    public void Normalize_PopulatesProviderMetadata()
    {
        var providerTime = new DateTimeOffset(2026, 4, 17, 14, 30, 0, TimeSpan.Zero);
        var tick = TiingoQuoteNormalizer.Normalize(
            symbol: "AAPL",
            last: 215.30m,
            bid: 215.25m,
            ask: 215.35m,
            currency: "USD",
            providerTimestamp: providerTime,
            sequence: 42);

        Assert.Equal("AAPL", tick.Symbol);
        Assert.Equal(215.30m, tick.Last);
        Assert.Equal(215.25m, tick.Bid);
        Assert.Equal(215.35m, tick.Ask);
        Assert.Equal("USD", tick.Currency);
        Assert.Equal("tiingo", tick.Provider);
        Assert.Equal(providerTime, tick.ProviderTimestamp);
        Assert.Equal(42, tick.Sequence);
        // IngressTimestamp is "now" — we just verify it was stamped.
        Assert.True(tick.IngressTimestamp <= DateTimeOffset.UtcNow);
        Assert.True(tick.IngressTimestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void ToLatestQuote_ProjectsAllScalarsAndDefaultsIsStaleFalse()
    {
        var tick = new RawTickV1
        {
            Symbol = "MSFT",
            Last = 432.10m,
            Bid = 432.05m,
            Ask = 432.15m,
            Currency = "USD",
            Provider = "tiingo",
            ProviderTimestamp = new DateTimeOffset(2026, 4, 17, 14, 31, 0, TimeSpan.Zero),
            IngressTimestamp = new DateTimeOffset(2026, 4, 17, 14, 31, 1, TimeSpan.Zero),
            Sequence = 100,
        };

        var latest = TiingoQuoteNormalizer.ToLatestQuote(tick);

        Assert.Equal(tick.Symbol, latest.Symbol);
        Assert.Equal(tick.Last, latest.Last);
        Assert.Equal(tick.Bid, latest.Bid);
        Assert.Equal(tick.Ask, latest.Ask);
        Assert.Equal(tick.Currency, latest.Currency);
        Assert.Equal(tick.Provider, latest.Provider);
        Assert.Equal(tick.ProviderTimestamp, latest.ProviderTimestamp);
        Assert.Equal(tick.IngressTimestamp, latest.IngressTimestamp);
        Assert.False(latest.IsStale);
    }
}
