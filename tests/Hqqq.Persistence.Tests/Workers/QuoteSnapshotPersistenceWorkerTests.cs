using Hqqq.Contracts.Events;
using Hqqq.Persistence.Feeds;
using Hqqq.Persistence.Options;
using Hqqq.Persistence.Tests.Fakes;
using Hqqq.Persistence.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Persistence.Tests.Workers;

public class QuoteSnapshotPersistenceWorkerTests
{
    private static QuoteSnapshotV1 Sample(string basketId, int seconds) => new()
    {
        BasketId = basketId,
        Timestamp = new DateTimeOffset(2026, 4, 16, 13, 30, seconds, TimeSpan.Zero),
        Nav = 600m + seconds,
        MarketProxyPrice = 500m,
        PremiumDiscountPct = -16m,
        StaleCount = 0,
        FreshCount = 3,
        MaxComponentAgeMs = 1d,
        QuoteQuality = "live",
    };

    private static (QuoteSnapshotPersistenceWorker worker, InMemoryQuoteSnapshotFeed feed, RecordingQuoteSnapshotWriter writer)
        Build(PersistenceOptions options, int failFirst = 0)
    {
        var feed = new InMemoryQuoteSnapshotFeed(options.SnapshotChannelCapacity);
        var writer = new RecordingQuoteSnapshotWriter(failFirst);
        var worker = new QuoteSnapshotPersistenceWorker(
            feed,
            writer,
            MsOptions.Create(options),
            NullLogger<QuoteSnapshotPersistenceWorker>.Instance);
        return (worker, feed, writer);
    }

    [Fact]
    public async Task Worker_FlushesWhenBatchSizeReached()
    {
        var options = new PersistenceOptions
        {
            SnapshotWriteBatchSize = 3,
            SnapshotFlushInterval = TimeSpan.FromSeconds(30),
            SnapshotChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        for (var i = 0; i < 3; i++)
            await feed.PublishAsync(Sample("HQQQ", i), CancellationToken.None);

        await WaitForAsync(() => writer.Batches.Count >= 1, TimeSpan.FromSeconds(2));

        feed.Complete();
        await run;

        var batch = Assert.Single(writer.Batches);
        Assert.Equal(3, batch.Count);
        Assert.Equal(3, worker.TotalRowsPersisted);
        Assert.Equal(0, worker.TotalFailureCount);
    }

    [Fact]
    public async Task Worker_FlushesPartialBatchAfterFlushInterval()
    {
        var options = new PersistenceOptions
        {
            SnapshotWriteBatchSize = 100,
            SnapshotFlushInterval = TimeSpan.FromMilliseconds(50),
            SnapshotChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        // Two snapshots well under the batch size; the flush-interval check
        // is performed on each new arrival, so the second publish (after
        // the interval elapses) triggers the flush.
        await feed.PublishAsync(Sample("HQQQ", 0), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        await feed.PublishAsync(Sample("HQQQ", 1), CancellationToken.None);

        await WaitForAsync(() => writer.Batches.Count >= 1, TimeSpan.FromSeconds(2));

        feed.Complete();
        await run;

        Assert.True(worker.TotalRowsPersisted >= 1,
            $"expected at least one row persisted, got {worker.TotalRowsPersisted}");
    }

    [Fact]
    public async Task Worker_FlushesRemainingBatchOnShutdown()
    {
        var options = new PersistenceOptions
        {
            SnapshotWriteBatchSize = 100,
            SnapshotFlushInterval = TimeSpan.FromSeconds(30),
            SnapshotChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        await feed.PublishAsync(Sample("HQQQ", 0), CancellationToken.None);
        await feed.PublishAsync(Sample("HQQQ", 1), CancellationToken.None);

        // Complete the channel so the consume loop exits; the finally-block
        // must flush the retained partial batch.
        feed.Complete();
        await run;

        var batch = Assert.Single(writer.Batches);
        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public async Task Worker_SurvivesWriterException_AndCountsFailures()
    {
        var options = new PersistenceOptions
        {
            SnapshotWriteBatchSize = 1,
            SnapshotFlushInterval = TimeSpan.FromSeconds(30),
            SnapshotChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options, failFirst: 2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        // First publish: batch reaches size 1, flush fails → retained.
        // Second publish: batch grows to 2, flush fails again → retained.
        // Third publish: batch grows to 3, flush succeeds → all three persisted.
        await feed.PublishAsync(Sample("HQQQ", 0), CancellationToken.None);
        await WaitForAsync(() => worker.TotalFailureCount >= 1, TimeSpan.FromSeconds(2));
        await feed.PublishAsync(Sample("HQQQ", 1), CancellationToken.None);
        await WaitForAsync(() => worker.TotalFailureCount >= 2, TimeSpan.FromSeconds(2));
        await feed.PublishAsync(Sample("HQQQ", 2), CancellationToken.None);

        await WaitForAsync(() => writer.Batches.Count >= 1, TimeSpan.FromSeconds(2));

        feed.Complete();
        await run;

        Assert.Equal(2, worker.TotalFailureCount);
        Assert.Equal(0, worker.ConsecutiveFailureCount);
        Assert.Equal(3, worker.TotalRowsPersisted);

        var batch = Assert.Single(writer.Batches);
        Assert.Equal(3, batch.Count);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
        }
    }
}
