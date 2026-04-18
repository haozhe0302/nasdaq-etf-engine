namespace Hqqq.Gateway.Services.Timescale;

/// <summary>
/// Pure response-shaping logic for <c>/api/history</c>. Given the raw
/// <see cref="HistoryRow"/> window, produces a JSON-ready payload whose
/// field layout matches the existing frontend adapter
/// (<c>src/hqqq-ui/src/lib/adapters.ts BHistoryResponse</c>) and the
/// legacy <c>hqqq-api HistoryModule</c> response. Kept free of I/O and
/// DI so the math is trivially unit-testable.
/// </summary>
/// <remarks>
/// Contract parity with legacy hqqq-api:
/// <list type="bullet">
///   <item><description>Series: stride downsample capped at <see cref="MaxSeriesPoints"/>, first and last always kept.</description></item>
///   <item><description>Basis bps: <c>(nav - marketProxyPrice) / marketProxyPrice * 10_000</c>.</description></item>
///   <item><description>Distribution: integer-rounded basis bps clamped to <c>[-10, +10]</c>, 21 deterministic buckets.</description></item>
///   <item><description>Completeness: <c>min(100, actualMinutes / (390 * daysLoaded) * 100)</c> (390 ≈ regular-session minutes per day).</description></item>
///   <item><description>Gaps: inter-sample intervals above <see cref="GapThreshold"/>.</description></item>
/// </list>
/// </remarks>
public static class HistoryResponseBuilder
{
    /// <summary>Maximum number of series points returned to the chart.</summary>
    public const int MaxSeriesPoints = 500;

    /// <summary>Regular-session minutes per trading day used for completeness math.</summary>
    public const int RegularSessionMinutesPerDay = 390;

    /// <summary>Threshold above which a sample interval counts as a gap.</summary>
    public static readonly TimeSpan GapThreshold = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyList<HistoryDistBucket> EmptyDistribution = BuildEmptyDistribution();

    public static HistoryResponse BuildEmpty(string range, DateOnly from, DateOnly to) =>
        new()
        {
            Range = range,
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd"),
            PointCount = 0,
            TotalPoints = 0,
            IsPartial = true,
            Series = Array.Empty<HistorySeriesPoint>(),
            TrackingError = new HistoryTrackingStats(),
            Distribution = EmptyDistribution,
            Diagnostics = new HistoryDiagnostics
            {
                Snapshots = 0,
                Gaps = 0,
                CompletenessPct = 0,
                DaysLoaded = 0,
            },
        };

    public static HistoryResponse Build(
        string range,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<HistoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return BuildEmpty(range, from, to);

        var totalPoints = rows.Count;
        var downsampled = Downsample(rows, MaxSeriesPoints);
        var series = new List<HistorySeriesPoint>(downsampled.Count);
        foreach (var r in downsampled)
        {
            series.Add(new HistorySeriesPoint
            {
                Time = r.Ts,
                Nav = r.Nav,
                MarketPrice = r.MarketProxyPrice,
            });
        }

        var trackingError = ComputeTrackingStats(rows);
        var distribution = ComputeDistribution(rows);

        var daysLoaded = CountDistinctUtcDays(rows);
        var expectedMinutes = Math.Max(1, to.DayNumber - from.DayNumber + 1) * RegularSessionMinutesPerDay;
        var actualMinutes = rows.Count > 1
            ? (rows[^1].Ts - rows[0].Ts).TotalMinutes
            : 0;
        var completenessPct = expectedMinutes > 0
            ? Math.Min(100, Math.Round(actualMinutes / expectedMinutes * 100, 1))
            : 0;

        var gaps = CountGaps(rows, GapThreshold);

        return new HistoryResponse
        {
            Range = range,
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd"),
            PointCount = series.Count,
            TotalPoints = totalPoints,
            IsPartial = daysLoaded < (to.DayNumber - from.DayNumber + 1),
            Series = series,
            TrackingError = trackingError,
            Distribution = distribution,
            Diagnostics = new HistoryDiagnostics
            {
                Snapshots = totalPoints,
                Gaps = gaps,
                CompletenessPct = completenessPct,
                DaysLoaded = daysLoaded,
            },
        };
    }

    // ── Downsampling (stride, keeps first and last) ─────

    internal static IReadOnlyList<T> Downsample<T>(IReadOnlyList<T> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;
        var result = new List<T>(maxPoints);
        var step = (double)(points.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints; i++)
        {
            var idx = Math.Min((int)Math.Round(i * step), points.Count - 1);
            result.Add(points[idx]);
        }
        return result;
    }

    // ── Tracking statistics ──────────────────────────────

    internal static HistoryTrackingStats ComputeTrackingStats(IReadOnlyList<HistoryRow> rows)
    {
        if (rows.Count == 0) return new HistoryTrackingStats();

        var basisBps = new double[rows.Count];
        var navVals = new double[rows.Count];
        var mktVals = new double[rows.Count];

        for (int i = 0; i < rows.Count; i++)
        {
            var nav = (double)rows[i].Nav;
            var mkt = (double)rows[i].MarketProxyPrice;
            navVals[i] = nav;
            mktVals[i] = mkt;
            basisBps[i] = mkt > 0 ? (nav - mkt) / mkt * 10_000 : 0;
        }

        var absBasis = basisBps.Select(Math.Abs).ToArray();

        return new HistoryTrackingStats
        {
            RmseBps = Math.Round(Math.Sqrt(basisBps.Select(b => b * b).Average()), 2),
            MaxAbsBasisBps = Math.Round(absBasis.Max(), 2),
            AvgAbsBasisBps = Math.Round(absBasis.Average(), 2),
            MaxDeviationPct = Math.Round(absBasis.Max() / 100, 4),
            Correlation = Math.Round(Pearson(navVals, mktVals), 5),
        };
    }

    private static double Pearson(double[] x, double[] y)
    {
        if (x.Length < 2) return 1;
        var n = x.Length;
        var mx = x.Average();
        var my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }
        var denom = Math.Sqrt(sxx * syy);
        return denom > 0 ? sxy / denom : 1;
    }

    // ── P/D distribution histogram ───────────────────────

    internal static IReadOnlyList<HistoryDistBucket> ComputeDistribution(IReadOnlyList<HistoryRow> rows)
    {
        if (rows.Count == 0) return EmptyDistribution;

        var buckets = new Dictionary<int, int>();
        foreach (var r in rows)
        {
            var mkt = (double)r.MarketProxyPrice;
            var bps = mkt > 0 ? (double)(r.Nav - r.MarketProxyPrice) / mkt * 10_000 : 0.0;
            var bin = (int)Math.Round(bps);
            bin = Math.Max(-10, Math.Min(10, bin));
            buckets[bin] = buckets.GetValueOrDefault(bin) + 1;
        }

        return Enumerable.Range(-10, 21)
            .Select(i => new HistoryDistBucket
            {
                Label = i.ToString(),
                Count = buckets.GetValueOrDefault(i),
            })
            .ToList();
    }

    private static IReadOnlyList<HistoryDistBucket> BuildEmptyDistribution()
        => Enumerable.Range(-10, 21)
            .Select(i => new HistoryDistBucket { Label = i.ToString(), Count = 0 })
            .ToList();

    // ── Gap detection ────────────────────────────────────

    private static int CountGaps(IReadOnlyList<HistoryRow> rows, TimeSpan threshold)
    {
        int gaps = 0;
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Ts - rows[i - 1].Ts > threshold) gaps++;
        return gaps;
    }

    private static int CountDistinctUtcDays(IReadOnlyList<HistoryRow> rows)
    {
        var seen = new HashSet<DateOnly>();
        foreach (var r in rows)
            seen.Add(DateOnly.FromDateTime(r.Ts.UtcDateTime));
        return seen.Count;
    }
}

// ── Response DTOs ──────────────────────────────────────
//
// Shapes are kept local to the gateway. Property names map to camelCase
// via HqqqJsonDefaults.Options to match the existing frontend adapter
// (src/hqqq-ui/src/lib/adapters.ts BHistoryResponse).

public sealed record HistoryResponse
{
    public required string Range { get; init; }
    public required string StartDate { get; init; }
    public required string EndDate { get; init; }
    public required int PointCount { get; init; }
    public required int TotalPoints { get; init; }
    public required bool IsPartial { get; init; }
    public required IReadOnlyList<HistorySeriesPoint> Series { get; init; }
    public required HistoryTrackingStats TrackingError { get; init; }
    public required IReadOnlyList<HistoryDistBucket> Distribution { get; init; }
    public required HistoryDiagnostics Diagnostics { get; init; }
}

public sealed record HistorySeriesPoint
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketPrice { get; init; }
}

public sealed record HistoryTrackingStats
{
    public double RmseBps { get; init; }
    public double MaxAbsBasisBps { get; init; }
    public double AvgAbsBasisBps { get; init; }
    public double MaxDeviationPct { get; init; }
    public double Correlation { get; init; }
}

public sealed record HistoryDistBucket
{
    public required string Label { get; init; }
    public required int Count { get; init; }
}

public sealed record HistoryDiagnostics
{
    public required int Snapshots { get; init; }
    public required int Gaps { get; init; }
    public required double CompletenessPct { get; init; }
    public required int DaysLoaded { get; init; }
}
