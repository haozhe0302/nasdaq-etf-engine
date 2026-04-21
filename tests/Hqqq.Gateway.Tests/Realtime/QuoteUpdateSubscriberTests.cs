using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Hubs;
using Hqqq.Gateway.Services.Realtime;
using Hqqq.Gateway.Tests.Fixtures;
using Hqqq.Observability.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hqqq.Gateway.Tests.Realtime;

/// <summary>
/// Phase 2-hotfix — covers the "degraded-not-crashed" contract for the
/// Redis pub/sub bridge: subscription failures must never escape
/// <c>ExecuteAsync</c>, the service must cooperate with cancellation
/// cleanly, and it must automatically recover once Redis becomes
/// reachable again.
/// </summary>
public class QuoteUpdateSubscriberTests
{
    [Fact]
    public async Task ExecuteAsync_RedisConnectionAlwaysFails_DoesNotEscape()
    {
        var channel = new FakeRedisQuoteUpdateChannel(
            factoryPerAttempt: _ => throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "no route to host"));

        var subscriber = BuildSubscriber(channel);

        using var cts = new CancellationTokenSource();
        await subscriber.StartAsync(cts.Token);

        // Let the retry loop spin through a few attempts.
        await WaitForAsync(() => channel.AttemptCount >= 3, TimeSpan.FromSeconds(2));

        // Subscriber must still be running — no unobserved exception
        // escaped to the host.
        Assert.True(channel.AttemptCount >= 3);
        Assert.False(subscriber.ExecuteTask!.IsFaulted,
            "QuoteUpdateSubscriber.ExecuteAsync must not surface Redis failures to the host.");

        cts.Cancel();
        await subscriber.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_RecoversAfterTransientFailure()
    {
        var attempts = 0;
        var channel = new FakeRedisQuoteUpdateChannel(
            factoryPerAttempt: _ =>
            {
                var n = Interlocked.Increment(ref attempts);
                if (n <= 2)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        $"transient attempt #{n}");
                }
            });

        var subscriber = BuildSubscriber(channel);

        using var cts = new CancellationTokenSource();
        await subscriber.StartAsync(cts.Token);

        // Once the third attempt succeeds, Subscribed flips true and the
        // subscriber settles into its "wait for shutdown" state.
        await WaitForAsync(() => channel.Subscribed, TimeSpan.FromSeconds(5));

        Assert.True(channel.Subscribed,
            "Subscriber should have recovered and established a live subscription.");
        Assert.True(channel.AttemptCount >= 3);

        cts.Cancel();
        await subscriber.StopAsync(CancellationToken.None);

        Assert.True(channel.Unsubscribed);
    }

    [Fact]
    public async Task ExecuteAsync_DeliversPayloadToBroadcaster_AfterSubscribe()
    {
        var channel = new FakeRedisQuoteUpdateChannel(factoryPerAttempt: _ => { });

        var hub = new RecordingHubContext<MarketHub>();
        var broadcaster = new QuoteUpdateBroadcaster(
            hub, new HqqqMetrics(), NullLogger<QuoteUpdateBroadcaster>.Instance);
        var subscriber = BuildSubscriber(channel, broadcaster);

        using var cts = new CancellationTokenSource();
        await subscriber.StartAsync(cts.Token);

        await WaitForAsync(() => channel.Subscribed, TimeSpan.FromSeconds(2));

        // Simulate a well-formed envelope hitting the pub/sub channel.
        const string payload = "{\"basketId\":\"HQQQ\",\"update\":{\"nav\":600,\"navChangePct\":0," +
                                "\"marketPrice\":500,\"premiumDiscountPct\":0,\"qqq\":500,\"qqqChangePct\":0," +
                                "\"basketValueB\":0,\"asOf\":\"2026-04-16T13:30:00Z\",\"movers\":[]," +
                                "\"freshness\":{\"symbolsTotal\":1,\"symbolsFresh\":1,\"symbolsStale\":0,\"freshPct\":100}," +
                                "\"feeds\":{\"webSocketConnected\":false,\"fallbackActive\":false,\"pricingActive\":true," +
                                "\"basketState\":\"active\",\"pendingActivationBlocked\":false}," +
                                "\"quoteState\":\"live\",\"isLive\":true,\"isFrozen\":false}}";
        await channel.PublishAsync(payload);

        await WaitForAsync(() => hub.Sends.Count > 0, TimeSpan.FromSeconds(2));

        Assert.Single(hub.Sends);
        Assert.Equal(QuoteUpdateBroadcaster.ClientEventName, hub.Sends[0].Method);

        cts.Cancel();
        await subscriber.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhileRetrying_CompletesCleanly()
    {
        var channel = new FakeRedisQuoteUpdateChannel(
            factoryPerAttempt: _ => throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect, "never reachable"));

        var subscriber = BuildSubscriber(channel);

        using var cts = new CancellationTokenSource();
        await subscriber.StartAsync(cts.Token);

        await WaitForAsync(() => channel.AttemptCount >= 1, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await subscriber.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(subscriber.ExecuteTask!.IsCompleted);
        Assert.False(subscriber.ExecuteTask.IsFaulted);
    }

    private static QuoteUpdateSubscriber BuildSubscriber(
        IRedisQuoteUpdateChannel channel,
        QuoteUpdateBroadcaster? broadcaster = null)
    {
        broadcaster ??= new QuoteUpdateBroadcaster(
            new RecordingHubContext<MarketHub>(),
            new HqqqMetrics(),
            NullLogger<QuoteUpdateBroadcaster>.Instance);

        var options = Options.Create(new GatewayRealtimeOptions
        {
            Enabled = true,
            // Tight delays keep the retry test fast without making it flaky.
            InitialRetryDelayMs = 10,
            MaxRetryDelayMs = 40,
        });

        return new QuoteUpdateSubscriber(
            channel, broadcaster, NullLogger<QuoteUpdateSubscriber>.Instance, options);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }
    }
}
