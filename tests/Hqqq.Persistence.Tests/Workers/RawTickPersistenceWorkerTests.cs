using Hqqq.Contracts.Events;
using Hqqq.Persistence.Feeds;
using Hqqq.Persistence.Options;
using Hqqq.Persistence.Tests.Fakes;
using Hqqq.Persistence.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Persistence.Tests.Workers;

public class RawTickPersistenceWorkerTests
{
    private static RawTickV1 Sample(string symbol, long sequence) => new()
    {
        Symbol = symbol,
        Last = 900m + sequence,
        Bid = 899m + sequence,
        Ask = 901m + sequence,
        Currency = "USD",
        Provider = "tiingo",
        ProviderTimestamp = new DateTimeOffset(2026, 4, 16, 13, 30, (int)(sequence % 60), TimeSpan.Zero),
        IngressTimestamp = new DateTimeOffset(2026, 4, 16, 13, 30, (int)(sequence % 60), 50, TimeSpan.Zero),
        Sequence = sequence,
    };

    private static (RawTickPersistenceWorker worker, InMemoryRawTickFeed feed, RecordingRawTickWriter writer)
        Build(PersistenceOptions options, int failFirst = 0)
    {
        var feed = new InMemoryRawTickFeed(options.RawTickChannelCapacity);
        var writer = new RecordingRawTickWriter(failFirst);
        var worker = new RawTickPersistenceWorker(
            feed,
            writer,
            MsOptions.Create(options),
            NullLogger<RawTickPersistenceWorker>.Instance);
        return (worker, feed, writer);
    }

    [Fact]
    public async Task Worker_FlushesWhenBatchSizeReached()
    {
        var options = new PersistenceOptions
        {
            RawTickWriteBatchSize = 3,
            RawTickFlushInterval = TimeSpan.FromSeconds(30),
            RawTickChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        for (var i = 0; i < 3; i++)
            await feed.PublishAsync(Sample("NVDA", i), CancellationToken.None);

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
            RawTickWriteBatchSize = 100,
            RawTickFlushInterval = TimeSpan.FromMilliseconds(50),
            RawTickChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        await feed.PublishAsync(Sample("NVDA", 0), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        await feed.PublishAsync(Sample("NVDA", 1), CancellationToken.None);

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
            RawTickWriteBatchSize = 100,
            RawTickFlushInterval = TimeSpan.FromSeconds(30),
            RawTickChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options);

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        await feed.PublishAsync(Sample("NVDA", 0), CancellationToken.None);
        await feed.PublishAsync(Sample("NVDA", 1), CancellationToken.None);

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
            RawTickWriteBatchSize = 1,
            RawTickFlushInterval = TimeSpan.FromSeconds(30),
            RawTickChannelCapacity = 16,
        };
        var (worker, feed, writer) = Build(options, failFirst: 2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.RunAsync(cts.Token);

        await feed.PublishAsync(Sample("NVDA", 0), CancellationToken.None);
        await WaitForAsync(() => worker.TotalFailureCount >= 1, TimeSpan.FromSeconds(2));
        await feed.PublishAsync(Sample("NVDA", 1), CancellationToken.None);
        await WaitForAsync(() => worker.TotalFailureCount >= 2, TimeSpan.FromSeconds(2));
        await feed.PublishAsync(Sample("NVDA", 2), CancellationToken.None);

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
