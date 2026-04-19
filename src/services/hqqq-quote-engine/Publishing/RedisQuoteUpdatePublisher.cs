using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.Observability.Metrics;
using Hqqq.QuoteEngine.Abstractions;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Phase 2D2 — publishes a slim <see cref="QuoteUpdateEnvelope"/> JSON payload
/// onto <see cref="RedisKeys.QuoteUpdateChannel"/>. Each gateway instance
/// subscribes to the channel and broadcasts the inner <see cref="QuoteUpdateDto"/>
/// over its own SignalR <c>/hubs/market</c> connections — multi-gateway-safe
/// without a SignalR Redis backplane.
///
/// Failures are isolated: the engine's materialize loop must keep running.
/// </summary>
public sealed class RedisQuoteUpdatePublisher : IQuoteUpdatePublisher
{
    private readonly IRedisChannelPublisher _channelPublisher;
    private readonly HqqqMetrics _metrics;
    private readonly ILogger<RedisQuoteUpdatePublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisQuoteUpdatePublisher(
        IRedisChannelPublisher channelPublisher,
        HqqqMetrics metrics,
        ILogger<RedisQuoteUpdatePublisher> logger)
        : this(channelPublisher, metrics, logger, HqqqJsonDefaults.Options)
    {
    }

    public RedisQuoteUpdatePublisher(
        IRedisChannelPublisher channelPublisher,
        HqqqMetrics metrics,
        ILogger<RedisQuoteUpdatePublisher> logger,
        JsonSerializerOptions jsonOptions)
    {
        _channelPublisher = channelPublisher;
        _metrics = metrics;
        _logger = logger;
        _jsonOptions = jsonOptions;
    }

    public async Task PublishAsync(string basketId, QuoteUpdateDto update, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(basketId))
            throw new ArgumentException("basketId must be non-empty", nameof(basketId));
        ArgumentNullException.ThrowIfNull(update);

        var envelope = new QuoteUpdateEnvelope
        {
            BasketId = basketId,
            Update = update,
        };

        string payload;
        try
        {
            payload = JsonSerializer.Serialize(envelope, _jsonOptions);
        }
        catch (Exception ex)
        {
            _metrics.QuoteUpdatePublishFailures.Add(1);
            _logger.LogWarning(ex,
                "Quote-update serialization failed for basket {BasketId}", basketId);
            return;
        }

        try
        {
            await _channelPublisher
                .PublishAsync(RedisKeys.QuoteUpdateChannel, payload, ct)
                .ConfigureAwait(false);
            _metrics.QuoteUpdatesPublished.Add(1);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.QuoteUpdatePublishFailures.Add(1);
            _logger.LogWarning(ex,
                "Redis quote-update publish failed for basket {BasketId} (channel {Channel})",
                basketId, RedisKeys.QuoteUpdateChannel);
        }
    }
}
