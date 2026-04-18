namespace Hqqq.Domain.Services;

/// <summary>
/// Pure helpers for summarizing per-symbol tick freshness. No clocks, no stores —
/// callers pass <c>now</c> and a stale threshold explicitly.
/// </summary>
public static class FreshnessSummarizer
{
    /// <summary>
    /// Compute fresh/stale counts and the average tick interval for a set of
    /// per-symbol observations. Symbols missing from <paramref name="observations"/>
    /// but present in <paramref name="trackedSymbols"/> are counted as stale.
    /// </summary>
    public static FreshnessSummary Summarize(
        IReadOnlyCollection<string> trackedSymbols,
        IReadOnlyDictionary<string, DateTimeOffset> observations,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        var total = trackedSymbols.Count;
        if (total == 0)
        {
            return new FreshnessSummary
            {
                SymbolsTotal = 0,
                SymbolsFresh = 0,
                SymbolsStale = 0,
                FreshPct = 0m,
                LastTickUtc = null,
                AvgTickIntervalMs = null,
            };
        }

        var staleThreshold = now - staleAfter;
        int fresh = 0, stale = 0;
        DateTimeOffset? lastTick = null;

        foreach (var symbol in trackedSymbols)
        {
            if (observations.TryGetValue(symbol, out var ts))
            {
                if (IsStale(ts, now, staleAfter)) stale++;
                else fresh++;

                if (lastTick is null || ts > lastTick) lastTick = ts;
            }
            else
            {
                stale++;
            }
        }

        var freshPct = Math.Round((decimal)fresh / total * 100m, 1);

        return new FreshnessSummary
        {
            SymbolsTotal = total,
            SymbolsFresh = fresh,
            SymbolsStale = stale,
            FreshPct = freshPct,
            LastTickUtc = lastTick,
            AvgTickIntervalMs = ComputeAverageIntervalMs(observations.Values),
        };
    }

    public static bool IsStale(DateTimeOffset receivedAtUtc, DateTimeOffset now, TimeSpan staleAfter)
        => (now - receivedAtUtc) > staleAfter;

    private static double? ComputeAverageIntervalMs(IEnumerable<DateTimeOffset> timestamps)
    {
        var arr = timestamps.ToArray();
        if (arr.Length < 2) return null;

        Array.Sort(arr);
        double totalMs = 0d;
        for (int i = 1; i < arr.Length; i++)
            totalMs += (arr[i] - arr[i - 1]).TotalMilliseconds;

        return Math.Round(totalMs / (arr.Length - 1), 2);
    }
}
