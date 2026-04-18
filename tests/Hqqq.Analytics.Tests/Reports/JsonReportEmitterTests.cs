using System.Text.Json;
using Hqqq.Analytics.Reports;

namespace Hqqq.Analytics.Tests.Reports;

public class JsonReportEmitterTests
{
    private static ReportSummary MakeSummary() => new()
    {
        BasketId = "HQQQ",
        RequestedStartUtc = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero),
        RequestedEndUtc = new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero),
        ActualFirstUtc = new DateTimeOffset(2026, 4, 17, 13, 30, 0, TimeSpan.Zero),
        ActualLastUtc = new DateTimeOffset(2026, 4, 17, 20, 0, 0, TimeSpan.Zero),
        PointCount = 3,
        MedianIntervalMs = 1000d,
        P95IntervalMs = 2000d,
        PointsPerMinute = 10d,
        StaleRatio = 0.25d,
        MaxComponentAgeMsP50 = 100d,
        MaxComponentAgeMsP95 = 500d,
        MaxComponentAgeMsMax = 1000d,
        QuoteQualityCounts = new Dictionary<string, long> { ["fresh"] = 2, ["stale"] = 1 },
        RmseBps = 42.5d,
        MaxAbsBasisBps = 80d,
        AvgAbsBasisBps = 30d,
        Correlation = 0.99d,
        TradingDaysCovered = 1,
        DaysCovered = 1,
        LargestGaps = new[]
        {
            new TimeGap(
                new DateTimeOffset(2026, 4, 17, 15, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 17, 15, 0, 10, TimeSpan.Zero),
                10_000d),
        },
        HasData = true,
    };

    [Fact]
    public async Task Emit_WritesParsableJsonWithExpectedFields()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"hqqq-report-{Guid.NewGuid():N}.json");
        try
        {
            var emitter = new JsonReportEmitter();
            await emitter.EmitAsync(MakeSummary(), tmp, CancellationToken.None);

            Assert.True(File.Exists(tmp));

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(tmp));
            var root = doc.RootElement;

            Assert.Equal("HQQQ", root.GetProperty("basketId").GetString());
            Assert.Equal(3, root.GetProperty("pointCount").GetInt64());
            Assert.Equal(0.25d, root.GetProperty("staleRatio").GetDouble(), 6);
            Assert.Equal(42.5d, root.GetProperty("rmseBps").GetDouble(), 6);
            Assert.True(root.GetProperty("hasData").GetBoolean());

            var gaps = root.GetProperty("largestGaps");
            Assert.Equal(1, gaps.GetArrayLength());
            Assert.Equal(10_000d, gaps[0].GetProperty("durationMs").GetDouble(), 6);

            // Quality counts stay keyed by the original quality strings.
            var counts = root.GetProperty("quoteQualityCounts");
            Assert.Equal(2, counts.GetProperty("fresh").GetInt64());
            Assert.Equal(1, counts.GetProperty("stale").GetInt64());
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Emit_CreatesParentDirectoryWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-report-dir-{Guid.NewGuid():N}", "nested");
        var tmp = Path.Combine(dir, "out.json");
        try
        {
            var emitter = new JsonReportEmitter();
            await emitter.EmitAsync(MakeSummary(), tmp, CancellationToken.None);

            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public async Task Emit_EmptySummary_ProducesHasDataFalseAndNoNaN()
    {
        var start = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero);
        var empty = Hqqq.Analytics.Reports.SnapshotQualityCalculator.Compute(
            "HQQQ", start, end,
            Array.Empty<Hqqq.Analytics.Timescale.QuoteSnapshotRecord>(),
            new[] { "stale" },
            topGapCount: 5);

        var tmp = Path.Combine(Path.GetTempPath(), $"hqqq-report-empty-{Guid.NewGuid():N}.json");
        try
        {
            var emitter = new JsonReportEmitter();
            await emitter.EmitAsync(empty, tmp, CancellationToken.None);

            var raw = await File.ReadAllTextAsync(tmp);
            Assert.DoesNotContain("NaN", raw);
            Assert.DoesNotContain("Infinity", raw);

            using var doc = JsonDocument.Parse(raw);
            Assert.False(doc.RootElement.GetProperty("hasData").GetBoolean());
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
