using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.Observability.Metrics;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Publishing;

public class RedisQuoteUpdatePublisherTests
{
    private static QuoteUpdateDto SampleUpdate(decimal nav = 600m) => new()
    {
        Nav = nav,
        NavChangePct = 1.5m,
        MarketPrice = 500m,
        PremiumDiscountPct = -16.6667m,
        Qqq = 500m,
        QqqChangePct = 1.01m,
        BasketValueB = 0.0006m,
        AsOf = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        LatestSeriesPoint = null,
        Movers = [],
        Freshness = new FreshnessDto
        {
            SymbolsTotal = 3,
            SymbolsFresh = 3,
            SymbolsStale = 0,
            FreshPct = 100m,
        },
        Feeds = new FeedInfoDto
        {
            WebSocketConnected = false,
            FallbackActive = false,
            PricingActive = true,
            BasketState = "active",
            PendingActivationBlocked = false,
        },
        QuoteState = "live",
        IsLive = true,
        IsFrozen = false,
    };

    [Fact]
    public async Task PublishAsync_PublishesEnvelopeJsonOnQuoteUpdateChannel()
    {
        var channelPublisher = new RecordingRedisChannelPublisher();
        var publisher = new RedisQuoteUpdatePublisher(
            channelPublisher,
            new HqqqMetrics(),
            NullLogger<RedisQuoteUpdatePublisher>.Instance);

        await publisher.PublishAsync("HQQQ", SampleUpdate(), CancellationToken.None);

        Assert.Single(channelPublisher.Published);
        var (channel, payload) = channelPublisher.Published.Single();

        Assert.Equal(RedisKeys.QuoteUpdateChannel, channel);
        Assert.Equal("hqqq:channel:quote-update", channel);

        // Round-trip: deserialize the envelope using the same camelCase
        // defaults the gateway subscriber will use.
        var envelope = JsonSerializer.Deserialize<QuoteUpdateEnvelope>(
            payload, HqqqJsonDefaults.Options);
        Assert.NotNull(envelope);
        Assert.Equal("HQQQ", envelope!.BasketId);
        Assert.Equal(600m, envelope.Update.Nav);
        Assert.Equal("live", envelope.Update.QuoteState);

        // Wire shape uses camelCase (locked frontend contract).
        Assert.Contains("\"basketId\":\"HQQQ\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"update\":", payload, StringComparison.Ordinal);
        Assert.Contains("\"nav\":600", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_RejectsEmptyBasketId()
    {
        var channelPublisher = new RecordingRedisChannelPublisher();
        var publisher = new RedisQuoteUpdatePublisher(
            channelPublisher,
            new HqqqMetrics(),
            NullLogger<RedisQuoteUpdatePublisher>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(" ", SampleUpdate(), CancellationToken.None));

        Assert.Empty(channelPublisher.Published);
    }

    [Fact]
    public async Task PublishAsync_SwallowsTransportFailure_AndDoesNotThrow()
    {
        var channelPublisher = new RecordingRedisChannelPublisher
        {
            ThrowOnPublish = new InvalidOperationException("redis down"),
        };
        var publisher = new RedisQuoteUpdatePublisher(
            channelPublisher,
            new HqqqMetrics(),
            NullLogger<RedisQuoteUpdatePublisher>.Instance);

        // Must not throw — the materialize loop must keep running even if
        // the pub/sub transport is wedged.
        await publisher.PublishAsync("HQQQ", SampleUpdate(), CancellationToken.None);

        Assert.Empty(channelPublisher.Published);
    }

    [Fact]
    public async Task PublishAsync_PropagatesCancellation()
    {
        // Cancellation is the one signal we must respect — it lets
        // BackgroundService shutdown complete promptly.
        var channelPublisher = new RecordingRedisChannelPublisher
        {
            ThrowOnPublish = new OperationCanceledException(),
        };
        var publisher = new RedisQuoteUpdatePublisher(
            channelPublisher,
            new HqqqMetrics(),
            NullLogger<RedisQuoteUpdatePublisher>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            publisher.PublishAsync("HQQQ", SampleUpdate(), cts.Token));
    }
}
