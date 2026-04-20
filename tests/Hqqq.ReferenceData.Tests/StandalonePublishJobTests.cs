using Hqqq.Contracts.Events;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Behavioural tests for <see cref="StandalonePublishJob"/>. The compacted
/// topic semantics mean publish-on-startup is the critical guarantee;
/// we also verify that publish failures do not crash the host (the job
/// just retries on the next interval).
/// </summary>
public class StandalonePublishJobTests
{
    [Fact]
    public async Task ExecuteAsync_PublishesActiveBasketOnStartup()
    {
        var captured = new CapturingPublisher();
        var job = BuildJob(captured);

        using var cts = new CancellationTokenSource();
        var run = job.StartAsync(cts.Token);

        // Wait for the startup publish to land. The republish interval
        // is clamped to 30s minimum, so a generous poll on the captured
        // publisher is enough — we never sleep that long here.
        await WaitForAsync(() => captured.Published.Count >= 1, TimeSpan.FromSeconds(5));

        cts.Cancel();
        await job.StopAsync(CancellationToken.None);
        await run;

        var ev = captured.Published[0];
        Assert.Equal("HQQQ", ev.BasketId);
        Assert.NotEmpty(ev.Constituents);
        Assert.NotEmpty(ev.PricingBasis.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCrash_WhenPublisherThrows()
    {
        var failing = new CapturingPublisher
        {
            ThrowOnPublish = new InvalidOperationException("broker down"),
        };
        var job = BuildJob(failing);

        using var cts = new CancellationTokenSource();
        var run = job.StartAsync(cts.Token);

        // Give the job a moment to attempt the startup publish (and
        // swallow the exception). We don't need to wait long because
        // the job's failure handler logs + returns synchronously.
        await Task.Delay(200);

        cts.Cancel();
        await job.StopAsync(CancellationToken.None);
        await run;

        Assert.NotNull(failing.LastError);
        Assert.Equal("broker down", failing.LastError!.Message);
    }

    private static StandalonePublishJob BuildJob(IBasketPublisher publisher)
    {
        var seed = SampleSeed();
        var repo = new SeedFileBasketRepository(seed);
        return new StandalonePublishJob(
            repo,
            publisher,
            Options.Create(new BasketSeedOptions { RepublishIntervalSeconds = 30 }),
            NullLogger<StandalonePublishJob>.Instance);
    }

    private static BasketSeed SampleSeed() => new()
    {
        BasketId = "HQQQ",
        Version = "v-test",
        AsOfDate = new DateOnly(2026, 4, 15),
        ScaleFactor = 1.0m,
        Fingerprint = "feedface" + new string('0', 56),
        Source = "test://memory",
        Constituents = new List<BasketSeedConstituent>
        {
            new() { Symbol = "AAPL", Name = "Apple", Sector = "Technology", SharesHeld = 100, ReferencePrice = 215.30m },
            new() { Symbol = "MSFT", Name = "Microsoft", Sector = "Technology", SharesHeld = 100, ReferencePrice = 432.10m },
        },
    };

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Predicate did not become true in time");
    }

    private sealed class CapturingPublisher : IBasketPublisher
    {
        public List<BasketActiveStateV1> Published { get; } = new();
        public Exception? ThrowOnPublish { get; set; }
        public Exception? LastError { get; private set; }

        public Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct)
        {
            if (ThrowOnPublish is not null)
            {
                LastError = ThrowOnPublish;
                throw ThrowOnPublish;
            }
            Published.Add(state);
            return Task.CompletedTask;
        }
    }
}
