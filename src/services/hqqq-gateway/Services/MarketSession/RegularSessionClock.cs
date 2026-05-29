namespace Hqqq.Gateway.Services.MarketSession;

/// <summary>
/// Dependency-light helper for the US equity regular trading session
/// (09:30–16:00 America/New_York, Mon–Fri) plus the 09:25–09:30 pre-open
/// reset window. Mirrors the persistence-side
/// <c>RegularSessionClock</c> definition so the write-side gate, the
/// read-side history filter, and the cleanup script all agree on what a
/// regular-session point is.
/// </summary>
/// <remarks>
/// Not a full exchange calendar: holidays and early closes are not
/// modeled. Weekend handling resolves to the most recent weekday session,
/// which is acceptable for the demo. DST is handled correctly because the
/// resolved <see cref="TimeZoneInfo"/> reports the right offset for the
/// 09:25 / 09:30 / 16:00 wall-clock times (none fall in a transition gap).
/// </remarks>
public sealed class RegularSessionClock
{
    private static readonly TimeOnly PreOpen = new(9, 25);
    private static readonly TimeOnly RegularOpen = new(9, 30);
    private static readonly TimeOnly RegularClose = new(16, 0);

    private readonly TimeZoneInfo _tz;

    public RegularSessionClock(TimeZoneInfo? timeZone = null)
    {
        _tz = timeZone ?? ResolveEasternTimeZone();
    }

    /// <summary>
    /// True when <paramref name="utc"/> falls inside the regular session
    /// (09:30 inclusive – 16:00 exclusive ET) on a weekday.
    /// </summary>
    public bool IsRegularSessionPoint(DateTimeOffset utc)
    {
        var local = ToLocal(utc);
        if (IsWeekend(local)) return false;

        var t = TimeOnly.FromDateTime(local.DateTime);
        return t >= RegularOpen && t < RegularClose;
    }

    /// <summary>Alias for <see cref="IsRegularSessionPoint"/>.</summary>
    public bool IsRegularSessionOpen(DateTimeOffset utc) => IsRegularSessionPoint(utc);

    /// <summary>
    /// True when <paramref name="utc"/> falls inside the pre-open reset
    /// window (09:25 inclusive – 09:30 exclusive ET) on a weekday. During
    /// this window the live chart should be cleared / show "waiting for
    /// market open".
    /// </summary>
    public bool IsPreOpenResetWindow(DateTimeOffset utc)
    {
        var local = ToLocal(utc);
        if (IsWeekend(local)) return false;

        var t = TimeOnly.FromDateTime(local.DateTime);
        return t >= PreOpen && t < RegularOpen;
    }

    /// <summary>
    /// Returns the UTC [open, close] bounds for the regular session on the
    /// given ET calendar date. Does not check whether that date is a
    /// weekday — callers that need that should filter first.
    /// </summary>
    public (DateTimeOffset openUtc, DateTimeOffset closeUtc) GetRegularSessionWindowForEtDate(DateOnly etDate)
    {
        var openLocal = etDate.ToDateTime(RegularOpen);
        var closeLocal = etDate.ToDateTime(RegularClose);
        return (ToUtcFromLocal(openLocal), ToUtcFromLocal(closeLocal));
    }

    /// <summary>
    /// Returns the UTC [open, close] bounds of the most recent regular
    /// session that has fully completed (close ≤ now). Walks backward over
    /// weekends; today's session counts only once 16:00 ET has passed.
    /// </summary>
    public (DateTimeOffset openUtc, DateTimeOffset closeUtc) GetMostRecentCompletedRegularSessionWindow(DateTimeOffset utcNow)
    {
        var local = ToLocal(utcNow);
        var date = DateOnly.FromDateTime(local.DateTime);
        var nowTime = TimeOnly.FromDateTime(local.DateTime);

        // If today is a weekday and the close has already passed, today's
        // session is the most recent completed one. Otherwise step back to
        // the prior calendar day and search from there.
        if (IsWeekend(local) || nowTime < RegularClose)
            date = date.AddDays(-1);

        while (IsWeekendDate(date))
            date = date.AddDays(-1);

        return GetRegularSessionWindowForEtDate(date);
    }

    /// <summary>Best-effort zone resolver tolerant of IANA and Windows ids.</summary>
    public static TimeZoneInfo ResolveEasternTimeZone()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }

    private DateTimeOffset ToLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc.ToUniversalTime(), _tz);

    private DateTimeOffset ToUtcFromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = _tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static bool IsWeekend(DateTimeOffset local) =>
        local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static bool IsWeekendDate(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
