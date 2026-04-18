using Hqqq.Analytics.Timescale;

namespace Hqqq.Analytics.Reports;

/// <summary>
/// Pure, deterministic computation of a <see cref="ReportSummary"/> from a
/// list of persisted <see cref="QuoteSnapshotRecord"/>s. No I/O, no randomness,
/// no clock access — given the same rows and options, always returns the
/// same summary. This is where the C4 "deterministic report" guarantee lives.
/// </summary>
/// <remarks>
/// Expects rows in ascending <c>Ts</c> order (as returned by the Timescale
/// reader); the calculator sorts defensively so test fixtures do not have
/// to mirror reader ordering.
/// </remarks>
public static class SnapshotQualityCalculator
{
    public static ReportSummary Compute(
        string basketId,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc,
        IReadOnlyList<QuoteSnapshotRecord> rows,
        IReadOnlyCollection<string> staleQualityStates,
        int topGapCount,
        long? rawTickCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basketId);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(staleQualityStates);
        if (topGapCount < 0) throw new ArgumentOutOfRangeException(nameof(topGapCount));

        if (rows.Count == 0)
        {
            return new ReportSummary
            {
                BasketId = basketId,
                RequestedStartUtc = requestedStartUtc,
                RequestedEndUtc = requestedEndUtc,
                ActualFirstUtc = null,
                ActualLastUtc = null,
                PointCount = 0,
                MedianIntervalMs = null,
                P95IntervalMs = null,
                PointsPerMinute = null,
                StaleRatio = 0d,
                MaxComponentAgeMsP50 = 0d,
                MaxComponentAgeMsP95 = 0d,
                MaxComponentAgeMsMax = 0d,
                QuoteQualityCounts = new Dictionary<string, long>(StringComparer.Ordinal),
                RmseBps = 0d,
                MaxAbsBasisBps = 0d,
                AvgAbsBasisBps = 0d,
                Correlation = null,
                TradingDaysCovered = 0,
                DaysCovered = 0,
                LargestGaps = Array.Empty<TimeGap>(),
                RawTickCount = rawTickCount,
                HasData = false,
            };
        }

        var ordered = rows.OrderBy(r => r.Ts).ToArray();

        var actualFirst = ordered[0].Ts;
        var actualLast = ordered[^1].Ts;
        long pointCount = ordered.Length;

        // ── intervals ──
        double? medianMs = null;
        double? p95Ms = null;
        double? pointsPerMinute = null;
        if (ordered.Length >= 2)
        {
            var intervals = new double[ordered.Length - 1];
            for (int i = 1; i < ordered.Length; i++)
                intervals[i - 1] = (ordered[i].Ts - ordered[i - 1].Ts).TotalMilliseconds;

            medianMs = Percentile(intervals, 0.50);
            p95Ms = Percentile(intervals, 0.95);

            var spanMinutes = (actualLast - actualFirst).TotalMinutes;
            if (spanMinutes > 0)
                pointsPerMinute = pointCount / spanMinutes;
        }

        // ── stale ratio ──
        var staleSet = new HashSet<string>(staleQualityStates, StringComparer.OrdinalIgnoreCase);
        long staleRows = 0;
        foreach (var r in ordered)
        {
            if (staleSet.Contains(r.QuoteQuality))
                staleRows++;
        }
        var staleRatio = (double)staleRows / pointCount;

        // ── max component age percentiles ──
        var ages = ordered.Select(r => r.MaxComponentAgeMs).ToArray();
        var ageP50 = Percentile(ages, 0.50) ?? 0d;
        var ageP95 = Percentile(ages, 0.95) ?? 0d;
        var ageMax = ages.Max();

        // ── quality counts (ordinal so keys are stable for JSON) ──
        var qualityCounts = ordered
            .GroupBy(r => r.QuoteQuality, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (long)g.LongCount(), StringComparer.Ordinal);

        // ── basis / tracking metrics ──
        var basisBps = new double[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            var nav = ordered[i].Nav;
            var market = ordered[i].MarketProxyPrice;
            basisBps[i] = nav == 0m
                ? 0d
                : (double)((market - nav) / nav) * 10_000d;
        }

        double sumSquared = 0d;
        double sumAbs = 0d;
        double maxAbs = 0d;
        foreach (var b in basisBps)
        {
            sumSquared += b * b;
            var abs = Math.Abs(b);
            sumAbs += abs;
            if (abs > maxAbs) maxAbs = abs;
        }
        var rmse = Math.Sqrt(sumSquared / basisBps.Length);
        var avgAbs = sumAbs / basisBps.Length;

        double? correlation = null;
        if (ordered.Length >= 2)
        {
            var navSeries = ordered.Select(r => (double)r.Nav).ToArray();
            var mktSeries = ordered.Select(r => (double)r.MarketProxyPrice).ToArray();
            correlation = PearsonCorrelation(navSeries, mktSeries);
        }

        // ── trading-day / days-covered ──
        var distinctUtcDates = new HashSet<DateOnly>();
        var distinctTradingDays = new HashSet<DateOnly>();
        foreach (var r in ordered)
        {
            var d = DateOnly.FromDateTime(r.Ts.UtcDateTime);
            distinctUtcDates.Add(d);

            var dow = r.Ts.UtcDateTime.DayOfWeek;
            if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday)
                distinctTradingDays.Add(d);
        }

        // ── largest gaps ──
        IReadOnlyList<TimeGap> largestGaps;
        if (ordered.Length < 2 || topGapCount == 0)
        {
            largestGaps = Array.Empty<TimeGap>();
        }
        else
        {
            var gaps = new List<TimeGap>(ordered.Length - 1);
            for (int i = 1; i < ordered.Length; i++)
            {
                var start = ordered[i - 1].Ts;
                var end = ordered[i].Ts;
                var durationMs = (end - start).TotalMilliseconds;
                gaps.Add(new TimeGap(start, end, durationMs));
            }
            largestGaps = gaps
                .OrderByDescending(g => g.DurationMs)
                .ThenBy(g => g.StartUtc)
                .Take(topGapCount)
                .ToArray();
        }

        return new ReportSummary
        {
            BasketId = basketId,
            RequestedStartUtc = requestedStartUtc,
            RequestedEndUtc = requestedEndUtc,
            ActualFirstUtc = actualFirst,
            ActualLastUtc = actualLast,
            PointCount = pointCount,
            MedianIntervalMs = medianMs,
            P95IntervalMs = p95Ms,
            PointsPerMinute = pointsPerMinute,
            StaleRatio = staleRatio,
            MaxComponentAgeMsP50 = ageP50,
            MaxComponentAgeMsP95 = ageP95,
            MaxComponentAgeMsMax = ageMax,
            QuoteQualityCounts = qualityCounts,
            RmseBps = rmse,
            MaxAbsBasisBps = maxAbs,
            AvgAbsBasisBps = avgAbs,
            Correlation = correlation,
            TradingDaysCovered = distinctTradingDays.Count,
            DaysCovered = distinctUtcDates.Count,
            LargestGaps = largestGaps,
            RawTickCount = rawTickCount,
            HasData = true,
        };
    }

    /// <summary>
    /// Linear-interpolation percentile (R-7, the Excel/NumPy default)
    /// on a non-null, non-empty sequence. Returns <c>null</c> when the
    /// input is empty so the caller can decide whether <c>null</c> or a
    /// zeroed default is more appropriate for a given metric.
    /// </summary>
    internal static double? Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return null;
        if (percentile <= 0) return values.Min();
        if (percentile >= 1) return values.Max();

        var sorted = values.ToArray();
        Array.Sort(sorted);

        var rank = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];

        var weight = rank - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    /// <summary>
    /// Pearson correlation coefficient on two equal-length series.
    /// Returns <c>null</c> when either series has zero variance (a
    /// constant stream has undefined correlation; reporting <c>null</c>
    /// keeps the JSON schema honest instead of emitting <c>NaN</c>).
    /// </summary>
    internal static double? PearsonCorrelation(double[] x, double[] y)
    {
        if (x.Length != y.Length) throw new ArgumentException("length mismatch");
        if (x.Length < 2) return null;

        double meanX = 0, meanY = 0;
        for (int i = 0; i < x.Length; i++)
        {
            meanX += x[i];
            meanY += y[i];
        }
        meanX /= x.Length;
        meanY /= y.Length;

        double num = 0, denX = 0, denY = 0;
        for (int i = 0; i < x.Length; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            num += dx * dy;
            denX += dx * dx;
            denY += dy * dy;
        }

        if (denX == 0 || denY == 0) return null;
        return num / Math.Sqrt(denX * denY);
    }
}
