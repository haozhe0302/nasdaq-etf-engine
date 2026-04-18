using Hqqq.Analytics.Timescale;

namespace Hqqq.Analytics.Tests.Fakes;

/// <summary>
/// Small helper for building deterministic snapshot fixtures.
/// </summary>
internal static class SnapshotFixture
{
    public static QuoteSnapshotRecord Row(
        DateTimeOffset ts,
        decimal nav = 100m,
        decimal market = 100m,
        double ageMs = 250d,
        string quality = "fresh",
        string basketId = "HQQQ",
        int stale = 0,
        int fresh = 10)
        => new()
        {
            BasketId = basketId,
            Ts = ts,
            Nav = nav,
            MarketProxyPrice = market,
            PremiumDiscountPct = nav == 0 ? 0m : (market - nav) / nav,
            StaleCount = stale,
            FreshCount = fresh,
            MaxComponentAgeMs = ageMs,
            QuoteQuality = quality,
        };

    public static DateTimeOffset T(int hour = 14, int minute = 0, int second = 0, int ms = 0, int day = 17)
        => new(2026, 4, day, hour, minute, second, ms, TimeSpan.Zero);
}
