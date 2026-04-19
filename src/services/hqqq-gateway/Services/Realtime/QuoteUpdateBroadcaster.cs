using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Hubs;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.Observability.Metrics;
using Microsoft.AspNetCore.SignalR;

namespace Hqqq.Gateway.Services.Realtime;

/// <summary>
/// Phase 2D2 — handles a single Redis pub/sub payload: validates the
/// <see cref="QuoteUpdateEnvelope"/> JSON and broadcasts the inner
/// <see cref="QuoteUpdateDto"/> over <see cref="MarketHub"/> using the locked
/// SignalR event name <c>"QuoteUpdate"</c>.
///
/// Pulled out of <see cref="QuoteUpdateSubscriber"/> so the dispatch path is
/// trivially unit-testable without standing up a Redis subscription.
/// </summary>
public sealed class QuoteUpdateBroadcaster
{
    /// <summary>
    /// SignalR client method name the frontend listens to. Locked by the
    /// existing UI contract (<c>hub.on("QuoteUpdate", ...)</c>); changing
    /// this would break every connected client.
    /// </summary>
    public const string ClientEventName = "QuoteUpdate";

    private readonly IHubContext<MarketHub> _hubContext;
    private readonly HqqqMetrics _metrics;
    private readonly ILogger<QuoteUpdateBroadcaster> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public QuoteUpdateBroadcaster(
        IHubContext<MarketHub> hubContext,
        HqqqMetrics metrics,
        ILogger<QuoteUpdateBroadcaster> logger)
        : this(hubContext, metrics, logger, HqqqJsonDefaults.Options)
    {
    }

    public QuoteUpdateBroadcaster(
        IHubContext<MarketHub> hubContext,
        HqqqMetrics metrics,
        ILogger<QuoteUpdateBroadcaster> logger,
        JsonSerializerOptions jsonOptions)
    {
        _hubContext = hubContext;
        _metrics = metrics;
        _logger = logger;
        _jsonOptions = jsonOptions;
    }

    public async Task DispatchAsync(string payload, CancellationToken ct)
    {
        _metrics.GatewayQuoteUpdatesReceived.Add(1);

        QuoteUpdateEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<QuoteUpdateEnvelope>(payload, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _metrics.GatewayQuoteUpdatesMalformed.Add(1);
            _logger.LogDebug(ex,
                "Dropping malformed quote-update payload from {Channel}",
                RedisKeys.QuoteUpdateChannel);
            return;
        }

        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.BasketId)
            || envelope.Update is null)
        {
            _metrics.GatewayQuoteUpdatesMalformed.Add(1);
            _logger.LogDebug(
                "Dropping incomplete quote-update envelope from {Channel}",
                RedisKeys.QuoteUpdateChannel);
            return;
        }

        try
        {
            await _hubContext.Clients.All
                .SendAsync(ClientEventName, envelope.Update, ct)
                .ConfigureAwait(false);
            _metrics.GatewaySignalrBroadcasts.Add(1);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — let the host complete cleanly.
        }
        catch (Exception ex)
        {
            _metrics.GatewaySignalrBroadcastFailures.Add(1);
            _logger.LogWarning(ex,
                "SignalR broadcast of {Event} failed for basket {BasketId}",
                ClientEventName, envelope.BasketId);
        }
    }
}
