using Hqqq.Api.Modules.System.Contracts;
using Hqqq.Api.Modules.System.Services;

namespace Hqqq.Api.Tests.System;

public class MetricsServiceTests
{
    // ── RollingWindow percentile tests ──────────────────

    [Fact]
    public void RollingWindow_SingleSample_ReturnsItForAllPercentiles()
    {
        var window = new RollingWindow(100);
        window.Record(42.0);

        var stats = window.GetStats();

        Assert.Equal(42.0, stats.P50);
        Assert.Equal(42.0, stats.P95);
        Assert.Equal(42.0, stats.P99);
        Assert.Equal(1, stats.SampleCount);
    }

    [Fact]
    public void RollingWindow_EmptyWindow_ReturnsEmptyStats()
    {
        var window = new RollingWindow(100);
        var stats = window.GetStats();

        Assert.Equal(0, stats.P50);
        Assert.Equal(0, stats.SampleCount);
    }

    [Fact]
    public void RollingWindow_KnownDistribution_PercentilesAreCorrect()
    {
        var window = new RollingWindow(200);

        for (int i = 1; i <= 100; i++)
            window.Record(i);

        var stats = window.GetStats();

        Assert.Equal(100, stats.SampleCount);
        Assert.InRange(stats.P50, 49, 52);
        Assert.InRange(stats.P95, 94, 96);
        Assert.InRange(stats.P99, 98, 100);
    }

    [Fact]
    public void RollingWindow_OverCapacity_EvictsOldest()
    {
        var window = new RollingWindow(5);

        for (int i = 1; i <= 10; i++)
            window.Record(i);

        var stats = window.GetStats();

        Assert.Equal(5, stats.SampleCount);
        Assert.InRange(stats.P50, 7, 9);
    }

    // ── MetricsService snapshot tests ───────────────────

    [Fact]
    public void Snapshot_ReturnsInitialGaugeZeros()
    {
        var svc = new MetricsService();
        var snap = svc.GetSnapshot();

        Assert.Equal(0, snap.SnapshotAgeMs);
        Assert.Equal(0, snap.PricedWeightCoverage);
        Assert.Equal(0, snap.StaleSymbolRatio);
        Assert.Null(snap.LastFailoverRecoverySeconds);
        Assert.Null(snap.LastActivationJumpBps);
        Assert.Equal(0, snap.TickToQuoteMs.SampleCount);
        Assert.Equal(0, snap.QuoteBroadcastMs.SampleCount);
    }

    [Fact]
    public void Snapshot_ReflectsGaugeUpdates()
    {
        var svc = new MetricsService();

        svc.SetSnapshotAge(123.456);
        svc.SetPricedWeightCoverage(0.9876);
        svc.SetStaleSymbolRatio(0.0234);

        var snap = svc.GetSnapshot();

        Assert.Equal(123.46, snap.SnapshotAgeMs);
        Assert.Equal(0.9876, snap.PricedWeightCoverage);
        Assert.Equal(0.0234, snap.StaleSymbolRatio);
    }

    [Fact]
    public void Snapshot_ReflectsHistogramObservations()
    {
        var svc = new MetricsService();

        for (int i = 0; i < 10; i++)
            svc.RecordQuoteBroadcast(i * 1.0);

        var snap = svc.GetSnapshot();

        Assert.Equal(10, snap.QuoteBroadcastMs.SampleCount);
        Assert.True(snap.QuoteBroadcastMs.P50 > 0);
    }

    [Fact]
    public void Snapshot_ReflectsCounters()
    {
        var svc = new MetricsService();

        var before = svc.GetSnapshot();
        svc.IncrementTicksIngested();
        svc.IncrementTicksIngested();
        svc.IncrementTicksIngested();
        svc.IncrementQuoteBroadcasts();
        svc.IncrementBasketActivations();
        var after = svc.GetSnapshot();

        Assert.Equal(3, after.TotalTicksIngested - before.TotalTicksIngested);
        Assert.Equal(1, after.TotalQuoteBroadcasts - before.TotalQuoteBroadcasts);
        Assert.Equal(1, after.TotalBasketActivations - before.TotalBasketActivations);
    }

    [Fact]
    public void Snapshot_RecordsActivationJump()
    {
        var svc = new MetricsService();

        svc.RecordActivationJump(12.5);

        var snap = svc.GetSnapshot();

        Assert.Equal(12.5, snap.LastActivationJumpBps);
    }

    [Fact]
    public void FallbackLifecycle_RecordsDuration()
    {
        var svc = new MetricsService();

        var before = svc.GetSnapshot();
        svc.OnFallbackActivated();
        Thread.Sleep(50);
        svc.OnFallbackDeactivated();
        var after = svc.GetSnapshot();

        Assert.NotNull(after.LastFailoverRecoverySeconds);
        Assert.True(after.LastFailoverRecoverySeconds > 0);
        Assert.Equal(1, after.TotalFallbackActivations - before.TotalFallbackActivations);
    }

    [Fact]
    public void FallbackDeactivated_WithoutActivation_DoesNotRecord()
    {
        var svc = new MetricsService();

        svc.OnFallbackDeactivated();

        var snap = svc.GetSnapshot();

        Assert.Null(snap.LastFailoverRecoverySeconds);
    }
}
