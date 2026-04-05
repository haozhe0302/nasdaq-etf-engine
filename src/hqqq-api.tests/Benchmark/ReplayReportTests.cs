using Hqqq.Api.Modules.Benchmark.Contracts;
using Hqqq.Bench;

namespace Hqqq.Api.Tests.Benchmark;

public class ReplayReportTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 4, 4, 14, 30, 0, TimeSpan.Zero);

    private static RecordedEvent Tick(int offsetSec, string symbol, decimal price, string source = "ws") =>
        new()
        {
            EventType = "tick",
            Timestamp = T0.AddSeconds(offsetSec),
            Symbol = symbol,
            Price = price,
            Source = source,
        };

    private static RecordedEvent Quote(int offsetSec, decimal nav, decimal mkt,
        int total, int stale, double broadcastMs, double tickToQuoteMs) =>
        new()
        {
            EventType = "quote",
            Timestamp = T0.AddSeconds(offsetSec),
            Nav = nav,
            MarketPrice = mkt,
            PremiumDiscountBps = Math.Round((nav - mkt) / mkt * 10000m, 2),
            SymbolsTotal = total,
            SymbolsStale = stale,
            BroadcastMs = broadcastMs,
            TickToQuoteMs = tickToQuoteMs,
        };

    private static RecordedEvent Transport(int offsetSec, string action) =>
        new()
        {
            EventType = "transport",
            Timestamp = T0.AddSeconds(offsetSec),
            Action = action,
        };

    private static RecordedEvent Activation(int offsetSec, double jumpBps) =>
        new()
        {
            EventType = "activation",
            Timestamp = T0.AddSeconds(offsetSec),
            Fingerprint = "fp-test",
            JumpBps = jumpBps,
        };

    [Fact]
    public void Aggregate_EmptyEvents_ReturnsZeros()
    {
        var report = ReplayEngine.Aggregate([]);

        Assert.Equal(0, report.TickCount);
        Assert.Equal(0, report.QuoteCount);
        Assert.Equal(0, report.BasisRmseBps);
    }

    [Fact]
    public void Aggregate_TicksAndQuotes_ComputesLatencyPercentiles()
    {
        var events = new List<RecordedEvent>();

        for (int i = 0; i < 5; i++)
            events.Add(Tick(i, "AAPL", 200m + i));

        events.Add(Quote(1, 487.00m, 487.05m, 100, 2, 1.0, 50));
        events.Add(Quote(2, 487.10m, 487.05m, 100, 0, 2.0, 60));
        events.Add(Quote(3, 487.20m, 487.15m, 100, 1, 1.5, 55));

        var report = ReplayEngine.Aggregate(events);

        Assert.Equal(5, report.TickCount);
        Assert.Equal(3, report.QuoteCount);
        Assert.True(report.TickToQuoteP50Ms > 0);
        Assert.True(report.TickToQuoteP95Ms >= report.TickToQuoteP50Ms);
        Assert.True(report.BroadcastP50Ms > 0);
    }

    [Fact]
    public void Aggregate_FailoverEvents_ComputesRecoveryDuration()
    {
        var events = new List<RecordedEvent>
        {
            Transport(0, "fallback_activated"),
            Transport(10, "ws_recovered"),
            Transport(30, "fallback_activated"),
            Transport(45, "ws_recovered"),
        };

        var report = ReplayEngine.Aggregate(events);

        Assert.Equal(2, report.FallbackActivationCount);
        Assert.Equal(2, report.RecoveryDurationsSeconds.Length);
        Assert.Equal(10.0, report.RecoveryDurationsSeconds[0], 1);
        Assert.Equal(15.0, report.RecoveryDurationsSeconds[1], 1);
        Assert.NotNull(report.MaxRecoverySeconds);
        Assert.Equal(15.0, report.MaxRecoverySeconds!.Value, 1);
    }

    [Fact]
    public void Aggregate_StaleRatio_ComputedCorrectly()
    {
        var events = new List<RecordedEvent>
        {
            Quote(1, 487m, 487m, 100, 5, 1, 50),
            Quote(2, 487m, 487m, 100, 10, 1, 50),
            Quote(3, 487m, 487m, 100, 0, 1, 50),
        };

        var report = ReplayEngine.Aggregate(events);

        Assert.Equal(0.05, report.AvgStaleSymbolRatio, 2);
        Assert.Equal(0.10, report.MaxStaleSymbolRatio, 2);
        Assert.InRange(report.PctQuotesWithStale, 66, 67);
    }

    [Fact]
    public void Aggregate_BasisRmse_ComputedFromNavVsMarket()
    {
        var events = new List<RecordedEvent>
        {
            Quote(1, 100.10m, 100.00m, 50, 0, 1, 50),
            Quote(2, 100.00m, 100.00m, 50, 0, 1, 50),
            Quote(3, 99.90m, 100.00m, 50, 0, 1, 50),
        };

        var report = ReplayEngine.Aggregate(events);

        Assert.True(report.BasisRmseBps > 0, "RMSE should be positive for non-zero basis");
        Assert.True(report.MaxAbsBasisBps > 0);
        Assert.True(report.AvgAbsBasisBps > 0);
    }

    [Fact]
    public void Aggregate_SessionMetadata_CapturedCorrectly()
    {
        var events = new List<RecordedEvent>
        {
            Tick(0, "AAPL", 200),
            Tick(0, "MSFT", 400),
            Tick(1, "AAPL", 201),
            Quote(2, 487m, 487m, 100, 0, 1, 50),
            Activation(3, 2.5),
        };

        var report = ReplayEngine.Aggregate(events);

        Assert.Equal(T0, report.SessionStart);
        Assert.Equal(T0.AddSeconds(3), report.SessionEnd);
        Assert.Equal(3, report.TickCount);
        Assert.Equal(1, report.QuoteCount);
        Assert.Equal(1, report.ActivationCount);
        Assert.Equal(2, report.SymbolsCovered);
    }

    [Fact]
    public void Aggregate_Percentile_SmallSample()
    {
        var values = new double[] { 10, 20, 30 };
        Assert.Equal(20, ReplayEngine.Pctl(values, 0.50));
    }

    [Fact]
    public void Report_ToMarkdown_ContainsExpectedSections()
    {
        var events = new List<RecordedEvent>
        {
            Tick(0, "AAPL", 200),
            Quote(1, 487m, 487.05m, 100, 2, 1.5, 45),
        };

        var report = ReplayEngine.Aggregate(events);
        var md = report.ToMarkdown();

        Assert.Contains("# HQQQ Benchmark Report", md);
        Assert.Contains("## Latency", md);
        Assert.Contains("## Failover", md);
        Assert.Contains("## Freshness", md);
        Assert.Contains("## Tracking / Basis vs QQQ", md);
    }

    [Fact]
    public void Report_ToJson_IsValidJson()
    {
        var events = new List<RecordedEvent>
        {
            Quote(1, 487m, 487m, 100, 0, 1, 50),
        };

        var report = ReplayEngine.Aggregate(events);
        var json = report.ToJson();

        var doc = global::System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("tickToQuoteP50Ms", out _));
        Assert.True(doc.RootElement.TryGetProperty("basisRmseBps", out _));
    }
}
