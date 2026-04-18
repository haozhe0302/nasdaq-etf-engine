namespace Hqqq.Gateway.Services.Timescale;

/// <summary>
/// Centralized mapping from the frontend-visible range tokens
/// (<c>1D, 5D, 1M, 3M, YTD, 1Y</c>) to a closed <c>[fromUtc, toUtc]</c>
/// window suitable for a <c>ts BETWEEN</c> predicate against
/// <c>quote_snapshots</c>. Window math mirrors the legacy
/// <c>hqqq-api HistoryModule.ResolveRange</c> so the gateway Timescale
/// path and the legacy proxy path agree on which rows belong in each
/// range.
/// </summary>
public static class HistoryRangeMap
{
    /// <summary>
    /// Canonical supported range tokens in display order. Exposed so
    /// error responses and docs can share the same list.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedRanges = new[]
    {
        "1D", "5D", "1M", "3M", "YTD", "1Y",
    };

    /// <summary>
    /// Tries to resolve <paramref name="range"/> (case-insensitive) to
    /// a UTC window anchored on <paramref name="todayUtc"/>. Returns
    /// <c>false</c> for any token not in <see cref="SupportedRanges"/>.
    /// The resulting window is end-inclusive: <c>fromUtc</c> is the
    /// start of the earliest calendar day and <c>toUtc</c> is the end
    /// of <paramref name="todayUtc"/>, so intraday rows (e.g. <c>1D</c>)
    /// are captured without a separate time-of-day parameter.
    /// </summary>
    public static bool TryResolve(
        string? range,
        DateTimeOffset todayUtc,
        out string normalizedRange,
        out DateTimeOffset fromUtc,
        out DateTimeOffset toUtc)
    {
        normalizedRange = range?.Trim().ToUpperInvariant() ?? string.Empty;
        fromUtc = default;
        toUtc = default;

        var today = new DateOnly(todayUtc.Year, todayUtc.Month, todayUtc.Day);
        DateOnly from;
        DateOnly to = today;

        switch (normalizedRange)
        {
            case "1D":
                from = today;
                break;
            case "5D":
                from = today.AddDays(-4);
                break;
            case "1M":
                from = today.AddMonths(-1);
                break;
            case "3M":
                from = today.AddMonths(-3);
                break;
            case "YTD":
                from = new DateOnly(today.Year, 1, 1);
                break;
            case "1Y":
                from = today.AddYears(-1);
                break;
            default:
                return false;
        }

        fromUtc = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
        toUtc = new DateTimeOffset(to.Year, to.Month, to.Day, 23, 59, 59, TimeSpan.Zero)
            .AddTicks(TimeSpan.TicksPerSecond - 1);
        return true;
    }
}
