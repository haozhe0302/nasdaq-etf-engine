namespace Hqqq.Persistence.MarketSession;

/// <summary>
/// Dependency-light helper for the US equity regular trading session
/// (09:30–16:00 America/New_York, Mon–Fri). Used by the persistence
/// write-gate so only regular-session quote snapshots are stored in
/// <c>quote_snapshots</c>.
/// </summary>
/// <remarks>
/// This is intentionally not a full exchange calendar: market holidays
/// and early-close days are not modeled. The cost of that simplification
/// is only that a handful of holiday rows may be written; the read-side
/// gateway filter and the cleanup script share the same definition, so
/// behavior stays consistent everywhere. DST is handled correctly because
/// the resolved <see cref="TimeZoneInfo"/> reports the right offset for
/// 09:30 / 16:00 wall-clock times (neither falls in a transition gap).
/// </remarks>
public sealed class RegularSessionClock
{
    private static readonly TimeOnly RegularOpen = new(9, 30);
    private static readonly TimeOnly RegularClose = new(16, 0);

    private readonly TimeZoneInfo _tz;

    public RegularSessionClock(TimeZoneInfo? timeZone = null)
    {
        _tz = timeZone ?? ResolveEasternTimeZone();
    }

    /// <summary>
    /// True when <paramref name="utc"/> falls inside the regular session
    /// (09:30 inclusive – 16:00 exclusive ET) on a weekday. This is the
    /// gate used to decide whether a snapshot is persisted to history.
    /// </summary>
    public bool IsRegularSessionPoint(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc.ToUniversalTime(), _tz);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var t = TimeOnly.FromDateTime(local.DateTime);
        return t >= RegularOpen && t < RegularClose;
    }

    /// <summary>Alias for <see cref="IsRegularSessionPoint"/> for readability at call sites.</summary>
    public bool IsRegularSessionOpen(DateTimeOffset utc) => IsRegularSessionPoint(utc);

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
}
