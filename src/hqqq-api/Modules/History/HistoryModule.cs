using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.History.Contracts;
using Hqqq.Api.Modules.History.Services;

namespace Hqqq.Api.Modules.History;

public static class HistoryModule
{
    private const int MaxSeriesPoints = 500;

    public static IServiceCollection AddHistoryModule(this IServiceCollection services)
    {
        services.AddSingleton<HistoryFileStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PricingOptions>>();
            var logger = sp.GetRequiredService<ILogger<HistoryFileStore>>();
            return new HistoryFileStore(opts.Value.HistoryDir, logger);
        });
        return services;
    }

    public static WebApplication MapHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/history", (string? range, HistoryFileStore store) =>
        {
            var r = range?.ToUpperInvariant() ?? "1D";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var (from, to) = ResolveRange(r, today);

            var rows = store.LoadRange(from, to);
            if (rows.Count == 0)
                return Results.Ok(EmptyResponse(r, from, to));

            var totalPoints = rows.Count;
            var downsampled = Downsample(rows, MaxSeriesPoints);
            var series = downsampled.Select(p => new HistorySeriesPoint
            {
                Time = p.Time,
                Nav = p.Nav,
                MarketPrice = p.MarketPrice,
            }).ToList();

            var tracking = ComputeTrackingStats(rows);
            var distribution = ComputeDistribution(rows);
            var daysLoaded = rows.Select(r => DateOnly.FromDateTime(r.Time.UtcDateTime))
                .Distinct().Count();

            var expectedMinutes = (to.DayNumber - from.DayNumber + 1) * 390;
            var actualMinutes = rows.Count > 1
                ? (rows[^1].Time - rows[0].Time).TotalMinutes : 0;
            var completeness = expectedMinutes > 0
                ? Math.Min(100, Math.Round(actualMinutes / expectedMinutes * 100, 1)) : 0;

            var gaps = CountGaps(rows, TimeSpan.FromSeconds(30));

            return Results.Ok(new HistoryResponse
            {
                Range = r,
                StartDate = from.ToString("yyyy-MM-dd"),
                EndDate = to.ToString("yyyy-MM-dd"),
                PointCount = series.Count,
                TotalPoints = totalPoints,
                IsPartial = daysLoaded < (to.DayNumber - from.DayNumber + 1),
                Series = series,
                TrackingError = tracking,
                Distribution = distribution,
                Diagnostics = new HistoryDiagnostics
                {
                    Snapshots = totalPoints,
                    Gaps = gaps,
                    CompletenessPct = completeness,
                    DaysLoaded = daysLoaded,
                },
            });
        })
        .WithName("GetHistory")
        .WithTags("History")
        .WithOpenApi();

        return app;
    }

    // ── Range resolution ─────────────────────────────────

    internal static (DateOnly from, DateOnly to) ResolveRange(string range, DateOnly today)
    {
        return range switch
        {
            "1D" => (today, today),
            "5D" => (today.AddDays(-4), today),
            "1M" => (today.AddMonths(-1), today),
            "3M" => (today.AddMonths(-3), today),
            "YTD" => (new DateOnly(today.Year, 1, 1), today),
            "1Y" => (today.AddYears(-1), today),
            _ => (today, today),
        };
    }

    // ── Downsampling (stride-based, always keeps first and last) ──

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

    // ── Tracking error statistics ────────────────────────

    internal static HistoryTrackingStats ComputeTrackingStats(IReadOnlyList<HistoryRow> rows)
    {
        if (rows.Count == 0)
            return new HistoryTrackingStats();

        var basisBps = new double[rows.Count];
        var navVals = new double[rows.Count];
        var mktVals = new double[rows.Count];

        for (int i = 0; i < rows.Count; i++)
        {
            var nav = (double)rows[i].Nav;
            var mkt = (double)rows[i].MarketPrice;
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

    internal static IReadOnlyList<HistoryDistBucket> ComputeDistribution(
        IReadOnlyList<HistoryRow> rows)
    {
        if (rows.Count == 0) return [];

        var basisBps = rows.Select(r =>
        {
            var mkt = (double)r.MarketPrice;
            return mkt > 0 ? (double)(r.Nav - r.MarketPrice) / mkt * 10_000 : 0.0;
        }).ToArray();

        var buckets = new Dictionary<int, int>();
        foreach (var b in basisBps)
        {
            var bin = (int)Math.Round(b);
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

    // ── Gap detection ────────────────────────────────────

    private static int CountGaps(IReadOnlyList<HistoryRow> rows, TimeSpan threshold)
    {
        int gaps = 0;
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Time - rows[i - 1].Time > threshold) gaps++;
        return gaps;
    }

    private static HistoryResponse EmptyResponse(string range, DateOnly from, DateOnly to) =>
        new()
        {
            Range = range,
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd"),
            PointCount = 0,
            TotalPoints = 0,
            IsPartial = true,
            Series = [],
            TrackingError = new HistoryTrackingStats(),
            Distribution = [],
            Diagnostics = new HistoryDiagnostics
            {
                Snapshots = 0, Gaps = 0, CompletenessPct = 0, DaysLoaded = 0,
            },
        };
}
