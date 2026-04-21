using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Jobs;

/// <summary>
/// Covers the background-job contract: the startup refresh runs once and
/// populates the store before the periodic loop kicks in.
/// </summary>
public class BasketRefreshJobTests
{
    [Fact]
    public async Task OnStartup_RunsSingleRefreshAndPublishes()
    {
        var source = new StubHoldingsSource();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var (job, store, publisher) = BuildJob(source, refreshSeconds: 0, republishSeconds: 0, startupSeconds: 5);

        // Execute once — timers are disabled so the job returns after the
        // startup refresh completes.
        using var cts = new CancellationTokenSource();
        await job.StartAsync(cts.Token);
        await job.StopAsync(cts.Token);

        Assert.NotNull(store.Current);
        Assert.Equal(1, source.FetchCount);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task OnStartup_WhenSourceUnavailable_DoesNotCrash()
    {
        var source = new StubHoldingsSource();
        source.Enqueue(HoldingsFetchResult.Unavailable("boom"));

        var (job, store, publisher) = BuildJob(source, refreshSeconds: 0, republishSeconds: 0, startupSeconds: 5);

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);

        // Source failed — store stays empty, publisher never called, but
        // host stays up waiting for the next (future) refresh.
        Assert.Null(store.Current);
        Assert.Empty(publisher.Published);
    }

    private static (BasketRefreshJob Job, ActiveBasketStore Store, CapturingPublisher Publisher) BuildJob(
        StubHoldingsSource source,
        int refreshSeconds,
        int republishSeconds,
        int startupSeconds)
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
            Refresh = new RefreshOptions
            {
                IntervalSeconds = refreshSeconds,
                RepublishIntervalSeconds = republishSeconds,
                StartupMaxWaitSeconds = startupSeconds,
            },
        });

        var validator = new HoldingsValidator(options);
        var store = new ActiveBasketStore();
        var publisher = new CapturingPublisher();
        var publishHealth = new PublishHealthTracker();
        var pipeline = new BasketRefreshPipeline(
            source, validator, store, publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance);

        var job = new BasketRefreshJob(pipeline, options, NullLogger<BasketRefreshJob>.Instance);
        return (job, store, publisher);
    }
}
