using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Stub;

// TODO: Phase 2B — replace with RedisConstituentsSource reading from Redis
public sealed class StubConstituentsSource : IConstituentsSource
{
    private static readonly DateTimeOffset StubTimestamp =
        new(2025, 1, 2, 14, 30, 0, TimeSpan.Zero);

    public Task<IResult> GetConstituentsAsync(CancellationToken ct)
    {
        var payload = new
        {
            holdings = new[]
            {
                new
                {
                    symbol = "AAPL", name = "Apple Inc.", sector = "Technology",
                    weight = 12.5m, shares = 1000, price = 185.00m, changePct = 1.20m,
                    marketValue = 185000m, sharesOrigin = "official", isStale = false,
                },
                new
                {
                    symbol = "MSFT", name = "Microsoft Corp.", sector = "Technology",
                    weight = 11.0m, shares = 800, price = 370.00m, changePct = -0.80m,
                    marketValue = 296000m, sharesOrigin = "official", isStale = false,
                },
                new
                {
                    symbol = "AMZN", name = "Amazon.com Inc.", sector = "Consumer Discretionary",
                    weight = 7.5m, shares = 600, price = 178.00m, changePct = 0.50m,
                    marketValue = 106800m, sharesOrigin = "official", isStale = false,
                },
            },
            concentration = new
            {
                top5Pct = 40.0m,
                top10Pct = 55.0m,
                top20Pct = 72.0m,
                sectorCount = 8,
                herfindahlIndex = 0.05m,
            },
            quality = new
            {
                totalSymbols = 100,
                officialSharesCount = 100,
                derivedSharesCount = 0,
                pricedCount = 100,
                staleCount = 0,
                priceCoveragePct = 100.0m,
                basketMode = "official",
            },
            source = new
            {
                anchorSource = "stub",
                tailSource = "stub",
                basketMode = "official",
                isDegraded = false,
                asOfDate = "2025-01-02",
                fingerprint = "stub-00000000",
            },
            asOf = StubTimestamp,
        };

        return Task.FromResult(Results.Ok(payload));
    }
}
