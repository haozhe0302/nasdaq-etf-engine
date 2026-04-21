using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Health;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hqqq.ReferenceData.Tests.Health;

/// <summary>
/// Unit-level coverage of <see cref="ActiveBasketHealthCheck"/> — the
/// three-state machine is further exercised in
/// <c>Services/PublishHealthTransitionTests</c> (pipeline-driven) and
/// <c>Health/ReadinessStatusCodeTests</c> (HTTP-driven). These cases
/// pin the simplest paths: empty store → Unhealthy, and first-activation
/// with a publish success in the same instant → Healthy with full
/// metadata.
/// </summary>
public class ActiveBasketHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenStoreEmpty_ReportsUnhealthy()
    {
        var check = BuildCheck(new ActiveBasketStore(), new PublishHealthTracker(), out _);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("no active basket", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_AfterActivationAndSuccessfulPublish_ReportsHealthyWithMetadata()
    {
        var store = new ActiveBasketStore();
        var tracker = new PublishHealthTracker();
        var now = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

        store.Set(new ActiveBasket
        {
            Snapshot = SnapshotBuilder.Build(count: 99, source: "live:file"),
            Fingerprint = "fp-deadbeef",
            ActivatedAtUtc = now,
        }, report: null);
        tracker.RecordAttempt(now);
        tracker.RecordSuccess(now, "fp-deadbeef");

        var check = BuildCheck(store, tracker, out _, now);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("HQQQ", result.Data["basketId"]);
        Assert.Equal("v-test", result.Data["version"]);
        Assert.Equal("fp-deadbeef", result.Data["currentFingerprint"]);
        Assert.Equal(99, result.Data["constituentCount"]);
        Assert.Equal("live:file", result.Data["source"]);
        Assert.Equal(true, result.Data["currentFingerprintPublished"]);
        Assert.Equal(0, result.Data["consecutivePublishFailures"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ActiveWithoutPublish_WithinGrace_ReportsDegraded()
    {
        var store = new ActiveBasketStore();
        var tracker = new PublishHealthTracker();
        var start = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

        store.Set(new ActiveBasket
        {
            Snapshot = SnapshotBuilder.Build(count: 60),
            Fingerprint = "fp-graceful",
            ActivatedAtUtc = start,
        }, report: null);

        var check = BuildCheck(store, tracker, out var clock, start);
        clock.Advance(TimeSpan.FromSeconds(10));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    private static ActiveBasketHealthCheck BuildCheck(
        ActiveBasketStore store,
        PublishHealthTracker tracker,
        out FakeTimeProvider clock,
        DateTimeOffset? start = null)
    {
        clock = new FakeTimeProvider(start ?? DateTimeOffset.UtcNow);
        var options = Options.Create(new ReferenceDataOptions
        {
            PublishHealth = new PublishHealthOptions
            {
                FirstActivationGraceSeconds = 60,
                DegradedAfterConsecutiveFailures = 1,
                UnhealthyAfterConsecutiveFailures = 5,
                MaxSilenceSeconds = 900,
            },
        });
        return new ActiveBasketHealthCheck(store, tracker, options, clock);
    }
}
