using Hqqq.Analytics.Reports;
using Hqqq.Analytics.Tests.Fakes;
using Hqqq.Analytics.Timescale;

namespace Hqqq.Analytics.Tests.Reports;

public class SnapshotQualityCalculatorTests
{
    private static readonly string[] StaleStates = new[] { "stale", "degraded" };

    [Fact]
    public void Empty_ReturnsHasDataFalseWithZeros()
    {
        var start = SnapshotFixture.T(14);
        var end = SnapshotFixture.T(15);

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ", start, end, Array.Empty<QuoteSnapshotRecord>(), StaleStates, topGapCount: 5);

        Assert.False(summary.HasData);
        Assert.Equal("HQQQ", summary.BasketId);
        Assert.Equal(start, summary.RequestedStartUtc);
        Assert.Equal(end, summary.RequestedEndUtc);
        Assert.Null(summary.ActualFirstUtc);
        Assert.Null(summary.ActualLastUtc);
        Assert.Equal(0, summary.PointCount);
        Assert.Null(summary.MedianIntervalMs);
        Assert.Null(summary.P95IntervalMs);
        Assert.Null(summary.PointsPerMinute);
        Assert.Equal(0d, summary.StaleRatio);
        Assert.Equal(0d, summary.MaxComponentAgeMsP50);
        Assert.Equal(0d, summary.MaxComponentAgeMsMax);
        Assert.Empty(summary.QuoteQualityCounts);
        Assert.Equal(0d, summary.RmseBps);
        Assert.Equal(0d, summary.MaxAbsBasisBps);
        Assert.Equal(0d, summary.AvgAbsBasisBps);
        Assert.Null(summary.Correlation);
        Assert.Equal(0, summary.TradingDaysCovered);
        Assert.Equal(0, summary.DaysCovered);
        Assert.Empty(summary.LargestGaps);
    }

    [Fact]
    public void ComputesPointCountAndActualBounds()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2)),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 5);

        Assert.True(summary.HasData);
        Assert.Equal(3, summary.PointCount);
        Assert.Equal(rows[0].Ts, summary.ActualFirstUtc);
        Assert.Equal(rows[2].Ts, summary.ActualLastUtc);
    }

    [Fact]
    public void ComputesIntervalPercentilesAndDensity()
    {
        // Intervals: 1000ms, 2000ms, 5000ms → median=2000, p95≈4700 (linear R-7)
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 3, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 8, 0)),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Equal(2000d, summary.MedianIntervalMs);
        Assert.NotNull(summary.P95IntervalMs);
        Assert.InRange(summary.P95IntervalMs!.Value, 4700d, 5000d);

        // 4 points over 8 seconds → 30 points per minute.
        Assert.NotNull(summary.PointsPerMinute);
        Assert.Equal(30d, summary.PointsPerMinute!.Value, 3);
    }

    [Fact]
    public void StaleRatio_MatchesConfiguredStates_CaseInsensitive()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0), quality: "fresh"),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1), quality: "Stale"),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2), quality: "DEGRADED"),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 3), quality: "fresh"),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Equal(0.5d, summary.StaleRatio);
        Assert.Equal(2L, summary.QuoteQualityCounts["fresh"]);
        Assert.Equal(1L, summary.QuoteQualityCounts["Stale"]);
        Assert.Equal(1L, summary.QuoteQualityCounts["DEGRADED"]);
    }

    [Fact]
    public void AgeStatistics_UseComponentAgeColumn()
    {
        var ages = new[] { 100d, 200d, 300d, 400d, 1000d };
        var rows = new QuoteSnapshotRecord[ages.Length];
        for (int i = 0; i < ages.Length; i++)
            rows[i] = SnapshotFixture.Row(SnapshotFixture.T(14, 0, i), ageMs: ages[i]);

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Equal(300d, summary.MaxComponentAgeMsP50);
        Assert.Equal(1000d, summary.MaxComponentAgeMsMax);
        // p95 on 5 values lies between 400 and 1000 (R-7 linear).
        Assert.InRange(summary.MaxComponentAgeMsP95, 400d, 1000d);
    }

    [Fact]
    public void BasisMetrics_AreHandComputable()
    {
        // market/nav ratios produce basis bps: +100, -100, 0
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0), nav: 100m, market: 101m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1), nav: 100m, market: 99m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2), nav: 100m, market: 100m),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        // abs basis bps = {100, 100, 0}
        Assert.Equal(100d, summary.MaxAbsBasisBps, 6);
        Assert.Equal((100 + 100 + 0) / 3d, summary.AvgAbsBasisBps, 6);
        // RMSE = sqrt((100^2 + 100^2 + 0) / 3) = sqrt(20000/3) ≈ 81.6497
        Assert.Equal(Math.Sqrt(20000d / 3d), summary.RmseBps, 6);
    }

    [Fact]
    public void Correlation_PerfectPositive_IsOne()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0), nav: 100m, market: 100m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1), nav: 101m, market: 101m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2), nav: 102m, market: 102m),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.NotNull(summary.Correlation);
        Assert.Equal(1d, summary.Correlation!.Value, 6);
    }

    [Fact]
    public void Correlation_PerfectNegative_IsMinusOne()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0), nav: 100m, market: 105m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1), nav: 101m, market: 104m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2), nav: 102m, market: 103m),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.NotNull(summary.Correlation);
        Assert.Equal(-1d, summary.Correlation!.Value, 6);
    }

    [Fact]
    public void Correlation_ConstantSeries_IsNull()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0), nav: 100m, market: 100m),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1), nav: 100m, market: 100m),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Null(summary.Correlation);
    }

    [Fact]
    public void TradingDayAndDaysCovered_SkipWeekends()
    {
        // Friday 2026-04-17, Saturday 2026-04-18, Monday 2026-04-20
        var rows = new[]
        {
            SnapshotFixture.Row(new DateTimeOffset(2026, 4, 17, 14, 0, 0, TimeSpan.Zero)),
            SnapshotFixture.Row(new DateTimeOffset(2026, 4, 18, 14, 0, 0, TimeSpan.Zero)),
            SnapshotFixture.Row(new DateTimeOffset(2026, 4, 20, 14, 0, 0, TimeSpan.Zero)),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Equal(3, summary.DaysCovered);
        Assert.Equal(2, summary.TradingDaysCovered);
    }

    [Fact]
    public void LargestGaps_ReturnsTopNByDuration()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)),       // +0s
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1)),       // +1s
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 11)),      // +10s  <-- largest
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 14)),      // +3s
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 16)),      // +2s
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 2);

        Assert.Equal(2, summary.LargestGaps.Count);
        Assert.Equal(10_000d, summary.LargestGaps[0].DurationMs);
        Assert.Equal(3_000d, summary.LargestGaps[1].DurationMs);
        Assert.Equal(rows[1].Ts, summary.LargestGaps[0].StartUtc);
        Assert.Equal(rows[2].Ts, summary.LargestGaps[0].EndUtc);
    }

    [Fact]
    public void TopGapCountZero_ReturnsEmptyGaps()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 5)),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0);

        Assert.Empty(summary.LargestGaps);
    }

    [Fact]
    public void UnsortedRows_AreTreatedAsSorted()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 2)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1)),
        };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 5);

        Assert.Equal(rows[1].Ts, summary.ActualFirstUtc);
        Assert.Equal(rows[0].Ts, summary.ActualLastUtc);
    }

    [Fact]
    public void RawTickCount_FlowsThroughWhenProvided()
    {
        var rows = new[] { SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)) };

        var summary = SnapshotQualityCalculator.Compute(
            "HQQQ",
            SnapshotFixture.T(14),
            SnapshotFixture.T(15),
            rows,
            StaleStates,
            topGapCount: 0,
            rawTickCount: 42L);

        Assert.Equal(42L, summary.RawTickCount);
    }
}
