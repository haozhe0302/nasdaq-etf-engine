namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Simple helper for US equity market hours. Not a full exchange calendar;
/// DST transitions and market holidays are not modelled in this phase.
/// Regular session: 09:30–16:00 America/New_York, Monday–Friday.
/// </summary>
public sealed class MarketHoursHelper
{
    private static readonly TimeOnly MarketOpen = new(9, 30);
    private static readonly TimeOnly MarketClose = new(16, 0);

    private readonly TimeZoneInfo _tz;

    public MarketHoursHelper(TimeZoneInfo tz) => _tz = tz;

    public bool IsMarketOpen()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var time = TimeOnly.FromDateTime(now);
        return time >= MarketOpen && time < MarketClose;
    }

    /// <summary>
    /// Returns the next market open in UTC.
    /// Advances to Monday if current day is Friday after close, Saturday, or Sunday.
    /// </summary>
    public DateTimeOffset NextMarketOpenUtc()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _tz);
        var today = nowLocal.Date;

        var candidate = today + MarketOpen.ToTimeSpan();

        if (nowLocal.DateTime >= candidate)
            candidate = candidate.AddDays(1);

        while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            candidate = candidate.AddDays(1);

        return new DateTimeOffset(
            DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), _tz.GetUtcOffset(candidate));
    }
}
