using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Health;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hqqq.ReferenceData.Tests.Services;

/// <summary>
/// Drives the Kafka publish-health state machine end-to-end through the
/// real refresh pipeline + <see cref="ActiveBasketHealthCheck"/> using a
/// <see cref="CapturingPublisher"/> wired to throw on demand and a
/// <see cref="FakeTimeProvider"/> so we can step the clock across the
/// configured thresholds. Pins Healthy → Degraded → Unhealthy on
/// sustained broker outage, first-activation-publish-fail degrades to
/// Degraded inside the grace window and Unhealthy after it, and recovery
/// resets everything to Healthy.
/// </summary>
public class PublishHealthTransitionTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Outage_HealthyToDegradedToUnhealthy()
    {
        var bench = new Bench(
            degradedAfter: 1,
            unhealthyAfter: 3,
            firstActivationGrace: 60,
            maxSilence: 3600);

        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));
        var first = await bench.Pipeline.RefreshAsync(CancellationToken.None);
        Assert.True(first.Success);
        Assert.Single(bench.Publisher.Published);

        bench.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(HealthStatus.Healthy, (await bench.CheckAsync()).Status);

        bench.Publisher.ThrowOnPublish = new InvalidOperationException("broker down");
        bench.Clock.Advance(TimeSpan.FromSeconds(5));
        var r1 = await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);
        Assert.True(r1.Success);
        var afterOneFailure = await bench.CheckAsync();
        Assert.Equal(HealthStatus.Degraded, afterOneFailure.Status);
        Assert.Equal(1, bench.PublishHealth.Snapshot.ConsecutivePublishFailures);

        bench.Clock.Advance(TimeSpan.FromSeconds(5));
        await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);
        bench.Clock.Advance(TimeSpan.FromSeconds(5));
        await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);

        var terminal = await bench.CheckAsync();
        Assert.Equal(HealthStatus.Unhealthy, terminal.Status);
        Assert.Equal(3, bench.PublishHealth.Snapshot.ConsecutivePublishFailures);

        Assert.Equal(Start, bench.PublishHealth.Snapshot.LastPublishOkUtc);
    }

    [Fact]
    public async Task FirstActivationPublishFail_GraceThenUnhealthy()
    {
        var bench = new Bench(
            degradedAfter: 1,
            unhealthyAfter: 99,
            firstActivationGrace: 60,
            maxSilence: 3600);

        bench.Publisher.ThrowOnPublish = new InvalidOperationException("broker down at startup");
        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var refresh = await bench.Pipeline.RefreshAsync(CancellationToken.None);
        Assert.True(refresh.Success);
        Assert.NotNull(bench.Store.Current);
        Assert.Null(bench.PublishHealth.Snapshot.LastPublishOkUtc);

        bench.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HealthStatus.Degraded, (await bench.CheckAsync()).Status);

        bench.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HealthStatus.Unhealthy, (await bench.CheckAsync()).Status);
    }

    [Fact]
    public async Task Recovery_ResetsCounterAndRestoresHealthy()
    {
        var bench = new Bench(
            degradedAfter: 1,
            unhealthyAfter: 5,
            firstActivationGrace: 60,
            maxSilence: 3600);

        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));
        await bench.Pipeline.RefreshAsync(CancellationToken.None);

        bench.Publisher.ThrowOnPublish = new InvalidOperationException("broker down");
        for (var i = 0; i < 3; i++)
        {
            bench.Clock.Advance(TimeSpan.FromSeconds(1));
            await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);
        }
        Assert.Equal(3, bench.PublishHealth.Snapshot.ConsecutivePublishFailures);
        Assert.Equal(HealthStatus.Degraded, (await bench.CheckAsync()).Status);
        var oldOk = bench.PublishHealth.Snapshot.LastPublishOkUtc;

        bench.Publisher.ThrowOnPublish = null;
        bench.Clock.Advance(TimeSpan.FromSeconds(10));
        var republish = await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);
        Assert.True(republish.Success);

        Assert.Equal(0, bench.PublishHealth.Snapshot.ConsecutivePublishFailures);
        Assert.NotNull(bench.PublishHealth.Snapshot.LastPublishOkUtc);
        Assert.True(bench.PublishHealth.Snapshot.LastPublishOkUtc > oldOk);
        Assert.Equal(HealthStatus.Healthy, (await bench.CheckAsync()).Status);
        Assert.Equal(bench.Store.Current!.Fingerprint,
            bench.PublishHealth.Snapshot.LastPublishedFingerprint);
    }

    private sealed class Bench
    {
        public FakeTimeProvider Clock { get; }
        public StubHoldingsSource Source { get; } = new();
        public CapturingPublisher Publisher { get; } = new();
        public ActiveBasketStore Store { get; } = new();
        public PublishHealthTracker PublishHealth { get; } = new();
        public BasketRefreshPipeline Pipeline { get; }
        public ActiveBasketHealthCheck Check { get; }

        public Bench(int degradedAfter, int unhealthyAfter, int firstActivationGrace, int maxSilence)
        {
            Clock = new FakeTimeProvider(Start);
            var options = new ReferenceDataOptions
            {
                Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
                PublishHealth = new PublishHealthOptions
                {
                    DegradedAfterConsecutiveFailures = degradedAfter,
                    UnhealthyAfterConsecutiveFailures = unhealthyAfter,
                    FirstActivationGraceSeconds = firstActivationGrace,
                    MaxSilenceSeconds = maxSilence,
                },
            };

            var built = PipelineBuilder.Build(
                source: Source,
                publisher: Publisher,
                store: Store,
                publishHealth: PublishHealth,
                options: options,
                clock: Clock);
            Pipeline = built.Pipeline;
            Check = new ActiveBasketHealthCheck(Store, PublishHealth, Options.Create(options), Clock);
        }

        public Task<HealthCheckResult> CheckAsync()
            => Check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }
}
