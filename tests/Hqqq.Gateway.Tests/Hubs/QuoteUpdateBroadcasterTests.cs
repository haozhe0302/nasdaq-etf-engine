using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Hubs;
using Hqqq.Gateway.Services.Realtime;
using Hqqq.Gateway.Tests.Fixtures;
using Hqqq.Infrastructure.Serialization;
using Hqqq.Observability.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Gateway.Tests.Hubs;

public class QuoteUpdateBroadcasterTests
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

    private static QuoteUpdateBroadcaster BuildBroadcaster(out RecordingHubContext<MarketHub> hub)
    {
        hub = new RecordingHubContext<MarketHub>();
        return new QuoteUpdateBroadcaster(
            hub, new HqqqMetrics(), NullLogger<QuoteUpdateBroadcaster>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_ValidEnvelope_BroadcastsQuoteUpdateEvent()
    {
        var broadcaster = BuildBroadcaster(out var hub);
        var update = SampleUpdate();
        var envelope = new QuoteUpdateEnvelope { BasketId = "HQQQ", Update = update };
        var json = JsonSerializer.Serialize(envelope, HqqqJsonDefaults.Options);

        await broadcaster.DispatchAsync(json, CancellationToken.None);

        Assert.Single(hub.Sends);
        var send = hub.Sends.Single();

        // Locked frontend contract: event name and DTO arg shape.
        Assert.Equal(QuoteUpdateBroadcaster.ClientEventName, send.Method);
        Assert.Equal("QuoteUpdate", send.Method);

        Assert.Single(send.Args);
        var arg = Assert.IsType<QuoteUpdateDto>(send.Args[0]);
        Assert.Equal(600m, arg.Nav);
        Assert.Equal("live", arg.QuoteState);
        Assert.True(arg.IsLive);
    }

    [Fact]
    public async Task DispatchAsync_RoundTripsPublisherProducedJson()
    {
        // Wire-shape regression guard: feed the broadcaster the exact JSON
        // an engine-side RedisQuoteUpdatePublisher would produce. If the
        // contract drifts (casing, envelope shape) this test catches it.
        var broadcaster = BuildBroadcaster(out var hub);
        var envelope = new QuoteUpdateEnvelope
        {
            BasketId = "HQQQ",
            Update = SampleUpdate(601.25m),
        };
        var json = JsonSerializer.Serialize(envelope, HqqqJsonDefaults.Options);
        Assert.Contains("\"basketId\":\"HQQQ\"", json, StringComparison.Ordinal);

        await broadcaster.DispatchAsync(json, CancellationToken.None);

        var send = Assert.Single(hub.Sends);
        var arg = Assert.IsType<QuoteUpdateDto>(send.Args[0]);
        Assert.Equal(601.25m, arg.Nav);
    }

    [Fact]
    public async Task DispatchAsync_MalformedJson_DropsAndDoesNotThrow()
    {
        var broadcaster = BuildBroadcaster(out var hub);

        await broadcaster.DispatchAsync("{not-json", CancellationToken.None);

        Assert.Empty(hub.Sends);
    }

    [Fact]
    public async Task DispatchAsync_MissingBasketId_DropsEnvelope()
    {
        var broadcaster = BuildBroadcaster(out var hub);
        var update = SampleUpdate();
        var updateJson = JsonSerializer.Serialize(update, HqqqJsonDefaults.Options);
        var json = $"{{\"update\":{updateJson}}}";

        await broadcaster.DispatchAsync(json, CancellationToken.None);

        Assert.Empty(hub.Sends);
    }

    [Fact]
    public async Task DispatchAsync_BroadcastFailure_DoesNotThrow()
    {
        var broadcaster = BuildBroadcaster(out var hub);
        hub.ThrowOnAllSend = new InvalidOperationException("hub down");

        var envelope = new QuoteUpdateEnvelope { BasketId = "HQQQ", Update = SampleUpdate() };
        var json = JsonSerializer.Serialize(envelope, HqqqJsonDefaults.Options);

        // SignalR transport failure must not propagate — the subscriber has
        // to keep listening to the channel for the next message.
        await broadcaster.DispatchAsync(json, CancellationToken.None);
    }
}
