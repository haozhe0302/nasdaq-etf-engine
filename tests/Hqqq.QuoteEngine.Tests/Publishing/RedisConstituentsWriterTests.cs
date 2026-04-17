using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Publishing;

public class RedisConstituentsWriterTests
{
    private static ConstituentsSnapshotDto Sample() => new()
    {
        Holdings =
        [
            new ConstituentHoldingDto
            {
                Symbol = "AAPL", Name = "Apple", Sector = "Tech",
                Weight = 50m, Shares = 1000m,
                Price = 200m, ChangePct = 1.2m, MarketValue = 200_000m,
                SharesOrigin = "official", IsStale = false,
            },
        ],
        Concentration = new ConcentrationDto
        {
            Top5Pct = 100m, Top10Pct = 100m, Top20Pct = 100m,
            SectorCount = 1, HerfindahlIndex = 1m,
        },
        Quality = new BasketQualityDto
        {
            TotalSymbols = 1, OfficialSharesCount = 1, DerivedSharesCount = 0,
            PricedCount = 1, StaleCount = 0, PriceCoveragePct = 100m,
            BasketMode = "official",
        },
        Source = new BasketSourceDto
        {
            AnchorSource = string.Empty, TailSource = string.Empty,
            BasketMode = "official", IsDegraded = false,
            AsOfDate = "2026-04-16", Fingerprint = "fp-1",
        },
        AsOf = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task WriteAsync_WritesJsonUnderNamespacedConstituentsKey()
    {
        var cache = new InMemoryRedisStringCache();
        var writer = new RedisConstituentsWriter(cache);

        await writer.WriteAsync("HQQQ", Sample(), CancellationToken.None);

        Assert.Single(cache.Writes);
        var (key, value) = cache.Writes.Single();
        Assert.Equal(RedisKeys.Constituents("HQQQ"), key);
        Assert.Equal("hqqq:constituents:HQQQ", key);

        var roundTrip = JsonSerializer.Deserialize<ConstituentsSnapshotDto>(
            value, HqqqJsonDefaults.Options);
        Assert.NotNull(roundTrip);
        Assert.Single(roundTrip!.Holdings);
        Assert.Equal("AAPL", roundTrip.Holdings[0].Symbol);
        Assert.Equal("fp-1", roundTrip.Source.Fingerprint);
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptyBasketId()
    {
        var cache = new InMemoryRedisStringCache();
        var writer = new RedisConstituentsWriter(cache);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.WriteAsync("", Sample(), CancellationToken.None));
        Assert.Empty(cache.Writes);
    }
}
