using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Stub;

// TODO: Phase 2B — replace with RedisQuoteSource reading latest snapshot from Redis
public sealed class StubQuoteSource : IQuoteSource
{
    private static readonly DateTimeOffset StubTimestamp =
        new(2025, 1, 2, 14, 30, 0, TimeSpan.Zero);

    public Task<IResult> GetQuoteAsync(CancellationToken ct)
    {
        var payload = new
        {
            nav = 100.00m,
            navChangePct = 0.25m,
            marketPrice = 99.95m,
            premiumDiscountPct = -0.05m,
            qqq = 450.00m,
            qqqChangePct = 0.30m,
            basketValueB = 1.5m,
            asOf = StubTimestamp,
            series = new[]
            {
                new { time = StubTimestamp.AddMinutes(-10), nav = 99.80m, market = 99.75m },
                new { time = StubTimestamp.AddMinutes(-5), nav = 99.90m, market = 99.85m },
                new { time = StubTimestamp, nav = 100.00m, market = 99.95m },
            },
            movers = new[]
            {
                new { symbol = "AAPL", name = "Apple Inc.", changePct = 1.20m, impact = 15.0m, direction = "up" },
                new { symbol = "MSFT", name = "Microsoft Corp.", changePct = -0.80m, impact = -10.0m, direction = "down" },
            },
            freshness = new
            {
                symbolsTotal = 100,
                symbolsFresh = 100,
                symbolsStale = 0,
                freshPct = 100.0m,
                lastTickUtc = StubTimestamp,
                avgTickIntervalMs = 250.0,
            },
            feeds = new
            {
                webSocketConnected = false,
                fallbackActive = false,
                pricingActive = false,
                basketState = "stub",
                pendingActivationBlocked = false,
                pendingBlockedReason = (string?)null,
                marketSessionState = "closed",
                isRegularSessionOpen = false,
                isTradingDay = true,
                nextOpenUtc = (DateTimeOffset?)null,
                sessionLabel = "stub",
            },
            quoteState = "stub",
            isLive = false,
            isFrozen = false,
            pauseReason = (string?)null,
        };

        return Task.FromResult(Results.Ok(payload));
    }
}
