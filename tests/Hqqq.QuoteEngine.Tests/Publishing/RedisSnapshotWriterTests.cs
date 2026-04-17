using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Publishing;

public class RedisSnapshotWriterTests
{
    private static QuoteSnapshotDto SampleSnapshot() => new()
    {
        Nav = 600m,
        NavChangePct = 1.5m,
        MarketPrice = 500m,
        PremiumDiscountPct = -16.6667m,
        Qqq = 500m,
        QqqChangePct = 1.01m,
        BasketValueB = 0.0006m,
        AsOf = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        Series = [],
        Movers = [],
        Freshness = new FreshnessDto
        {
            SymbolsTotal = 3,
            SymbolsFresh = 3,
            SymbolsStale = 0,
            FreshPct = 100m,
        },
        Feeds = new FeedInfoDto
        {
            WebSocketConnected = false,
            FallbackActive = false,
            PricingActive = true,
            BasketState = "active",
            PendingActivationBlocked = false,
        },
        QuoteState = "live",
        IsLive = true,
        IsFrozen = false,
    };

    [Fact]
    public async Task WriteAsync_WritesJsonUnderNamespacedSnapshotKey()
    {
        var cache = new InMemoryRedisStringCache();
        var writer = new RedisSnapshotWriter(cache);

        await writer.WriteAsync("HQQQ", SampleSnapshot(), CancellationToken.None);

        Assert.Single(cache.Writes);
        var (key, value) = cache.Writes.Single();
        Assert.Equal(RedisKeys.Snapshot("HQQQ"), key);
        Assert.Equal("hqqq:snapshot:HQQQ", key);

        var roundTrip = JsonSerializer.Deserialize<QuoteSnapshotDto>(value, HqqqJsonDefaults.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(600m, roundTrip!.Nav);
        Assert.Equal(500m, roundTrip.MarketPrice);
        Assert.Equal("live", roundTrip.QuoteState);
    }

    [Fact]
    public async Task WriteAsync_OverwritesPreviousValue()
    {
        var cache = new InMemoryRedisStringCache();
        var writer = new RedisSnapshotWriter(cache);

        await writer.WriteAsync("HQQQ", SampleSnapshot(), CancellationToken.None);
        var second = SampleSnapshot() with { Nav = 601m };
        await writer.WriteAsync("HQQQ", second, CancellationToken.None);

        Assert.Equal(2, cache.Writes.Count);
        var latest = JsonSerializer.Deserialize<QuoteSnapshotDto>(
            cache.Values[RedisKeys.Snapshot("HQQQ")],
            HqqqJsonDefaults.Options);
        Assert.Equal(601m, latest!.Nav);
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptyBasketId()
    {
        var cache = new InMemoryRedisStringCache();
        var writer = new RedisSnapshotWriter(cache);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.WriteAsync(" ", SampleSnapshot(), CancellationToken.None));
        Assert.Empty(cache.Writes);
    }
}
