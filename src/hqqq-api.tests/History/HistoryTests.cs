using Hqqq.Api.Modules.History.Contracts;
using Hqqq.Api.Modules.History.Services;
using Hqqq.Api.Modules.History;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Api.Tests.History;

public class HistoryTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 4, 4, 14, 30, 0, TimeSpan.Zero);

    private static HistoryRow Row(int offsetSec, decimal nav, decimal mkt,
        int total = 100, int stale = 0) =>
        new()
        {
            Time = T0.AddSeconds(offsetSec),
            Nav = nav,
            MarketPrice = mkt,
            SymbolsTotal = total,
            SymbolsStale = stale,
        };

    // ── FileStore write/read roundtrip ───────────────────

    [Fact]
    public void FileStore_WriteAndRead_Roundtrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-hist-{Guid.NewGuid():N}");
        try
        {
            using var store = new HistoryFileStore(dir,
                NullLogger<HistoryFileStore>.Instance);

            store.Append(Row(0, 487.00m, 487.05m));
            store.Append(Row(5, 487.10m, 487.05m));
            store.Append(Row(10, 487.20m, 487.10m));
            store.Dispose();

            using var store2 = new HistoryFileStore(dir,
                NullLogger<HistoryFileStore>.Instance);

            var date = DateOnly.FromDateTime(T0.UtcDateTime);
            var rows = store2.LoadRange(date, date);

            Assert.Equal(3, rows.Count);
            Assert.Equal(487.00m, rows[0].Nav);
            Assert.Equal(487.20m, rows[2].Nav);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileStore_CorruptLine_SkippedWithoutAbortingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-hist-{Guid.NewGuid():N}");
        try
        {
            using var store = new HistoryFileStore(dir,
                NullLogger<HistoryFileStore>.Instance);

            store.Append(Row(0, 487.00m, 487.05m));
            store.Append(Row(5, 487.10m, 487.05m));
            store.Dispose();

            var date = DateOnly.FromDateTime(T0.UtcDateTime);
            var filePath = Path.Combine(dir, date.ToString("yyyy-MM-dd"), "quotes.jsonl");
            var lines = File.ReadAllLines(filePath).ToList();
            lines.Insert(1, "{corrupt json here");
            lines.Insert(2, "");
            File.WriteAllLines(filePath, lines);

            using var store2 = new HistoryFileStore(dir,
                NullLogger<HistoryFileStore>.Instance);
            var rows = store2.LoadRange(date, date);

            Assert.Equal(2, rows.Count);
            Assert.Equal(487.00m, rows[0].Nav);
            Assert.Equal(487.10m, rows[1].Nav);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileStore_EmptyRange_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-hist-{Guid.NewGuid():N}");
        try
        {
            using var store = new HistoryFileStore(dir,
                NullLogger<HistoryFileStore>.Instance);

            var rows = store.LoadRange(
                new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1));

            Assert.Empty(rows);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ── Downsampling ─────────────────────────────────────

    [Fact]
    public void Downsample_SmallDataset_ReturnsAll()
    {
        var points = Enumerable.Range(0, 10).ToList();
        var result = HistoryModule.Downsample(points, 500);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Downsample_LargeDataset_ReducesToMax()
    {
        var points = Enumerable.Range(0, 2000).ToList();
        var result = HistoryModule.Downsample(points, 500);
        Assert.Equal(500, result.Count);
        Assert.Equal(0, result[0]);
        Assert.Equal(1999, result[^1]);
    }

    [Fact]
    public void Downsample_PreservesFirstAndLast()
    {
        var points = Enumerable.Range(0, 1000).ToList();
        var result = HistoryModule.Downsample(points, 100);
        Assert.Equal(0, result[0]);
        Assert.Equal(999, result[^1]);
    }

    // ── Range resolution ─────────────────────────────────

    [Fact]
    public void ResolveRange_1D_ReturnsSameDay()
    {
        var today = new DateOnly(2026, 4, 4);
        var (from, to) = HistoryModule.ResolveRange("1D", today);
        Assert.Equal(today, from);
        Assert.Equal(today, to);
    }

    [Fact]
    public void ResolveRange_5D_Returns5DaySpan()
    {
        var today = new DateOnly(2026, 4, 4);
        var (from, to) = HistoryModule.ResolveRange("5D", today);
        Assert.Equal(new DateOnly(2026, 3, 31), from);
        Assert.Equal(today, to);
    }

    [Fact]
    public void ResolveRange_YTD_StartsJan1()
    {
        var today = new DateOnly(2026, 4, 4);
        var (from, _) = HistoryModule.ResolveRange("YTD", today);
        Assert.Equal(new DateOnly(2026, 1, 1), from);
    }

    // ── Tracking stats ───────────────────────────────────

    [Fact]
    public void TrackingStats_NonZeroBasis_ComputesRmse()
    {
        var rows = new[]
        {
            Row(0, 100.10m, 100.00m),
            Row(1, 100.00m, 100.00m),
            Row(2, 99.90m, 100.00m),
        };

        var stats = HistoryModule.ComputeTrackingStats(rows);

        Assert.True(stats.RmseBps > 0);
        Assert.True(stats.MaxAbsBasisBps > 0);
        Assert.True(stats.Correlation > 0.99);
    }

    [Fact]
    public void TrackingStats_PerfectTrack_ZeroBasis()
    {
        var rows = new[]
        {
            Row(0, 100m, 100m),
            Row(1, 101m, 101m),
            Row(2, 102m, 102m),
        };

        var stats = HistoryModule.ComputeTrackingStats(rows);

        Assert.Equal(0, stats.RmseBps);
        Assert.Equal(0, stats.MaxAbsBasisBps);
        Assert.Equal(1, stats.Correlation);
    }

    // ── Distribution ─────────────────────────────────────

    [Fact]
    public void Distribution_BucketsAreProduced()
    {
        var rows = new[]
        {
            Row(0, 100.01m, 100.00m),
            Row(1, 100.02m, 100.00m),
            Row(2, 99.99m, 100.00m),
        };

        var dist = HistoryModule.ComputeDistribution(rows);

        Assert.True(dist.Count > 0);
        Assert.True(dist.Any(b => b.Count > 0));
    }

    [Fact]
    public void Distribution_EmptyRows_ReturnsEmpty()
    {
        var dist = HistoryModule.ComputeDistribution([]);
        Assert.Empty(dist);
    }
}
