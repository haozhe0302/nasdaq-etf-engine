namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/MarketHoursHelper.cs</c>.
/// Simple helper for US equity regular session (09:30–16:00
/// America/New_York, Mon–Fri). Not a full exchange calendar: holidays
/// and DST transitions are intentionally unmodeled — the lifecycle
/// scheduler's activation gate is "is the market open right now?" and
/// is naturally self-correcting on the next scheduler tick if it fires
/// a couple of minutes early.
/// </summary>
public sealed class MarketHoursHelper
{
    private static readonly TimeOnly RegularOpen = new(9, 30);
    private static readonly TimeOnly RegularClose = new(16, 0);

    private readonly TimeZoneInfo _tz;
    private readonly TimeProvider _clock;

    public MarketHoursHelper(TimeZoneInfo timeZone, TimeProvider? clock = null)
    {
        _tz = timeZone;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Best-effort zone resolver tolerant of IANA and Windows ids.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return TryFindZone("America/New_York", "Eastern Standard Time")
                ?? TimeZoneInfo.Utc;

        try { return TimeZoneInfo.FindSystemTimeZoneById(configured); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return TryFindZone(configured, "America/New_York", "Eastern Standard Time")
            ?? TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo? TryFindZone(params string[] candidates)
    {
        foreach (var id in candidates)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* fall through */ }
        }
        return null;
    }

    public bool IsMarketOpen()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _tz);
        if (nowLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        var t = TimeOnly.FromDateTime(nowLocal.DateTime);
        return t >= RegularOpen && t < RegularClose;
    }

    /// <summary>Next wall-clock market open in the configured zone, returned as a zoned offset.</summary>
    public DateTimeOffset NextMarketOpen()
    {
        var nowUtc = _clock.GetUtcNow();
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _tz);
        var today = nowLocal.Date;

        var candidate = today + RegularOpen.ToTimeSpan();
        if (nowLocal.DateTime >= candidate) candidate = candidate.AddDays(1);
        while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            candidate = candidate.AddDays(1);

        return new DateTimeOffset(
            DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified),
            _tz.GetUtcOffset(candidate));
    }
}
