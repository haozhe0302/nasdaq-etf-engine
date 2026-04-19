using Hqqq.Infrastructure.Redis;
using StackExchange.Redis;

namespace Hqqq.Gateway.Services.Realtime;

/// <summary>
/// Phase 2D2 — subscribes to <see cref="RedisKeys.QuoteUpdateChannel"/>
/// and forwards each payload to <see cref="QuoteUpdateBroadcaster"/>, which
/// owns deserialization, validation, and SignalR fan-out.
///
/// This BackgroundService is intentionally thin: separating wiring from
/// dispatch keeps the broadcast path trivially unit-testable without a real
/// Redis subscription.
/// </summary>
public sealed class QuoteUpdateSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly QuoteUpdateBroadcaster _broadcaster;
    private readonly ILogger<QuoteUpdateSubscriber> _logger;

    private ChannelMessageQueue? _queue;

    public QuoteUpdateSubscriber(
        IConnectionMultiplexer multiplexer,
        QuoteUpdateBroadcaster broadcaster,
        ILogger<QuoteUpdateSubscriber> logger)
    {
        _multiplexer = multiplexer;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _multiplexer.GetSubscriber();
        var channel = RedisChannel.Literal(RedisKeys.QuoteUpdateChannel);

        _queue = await subscriber.SubscribeAsync(channel).ConfigureAwait(false);
        _logger.LogInformation(
            "QuoteUpdateSubscriber listening on Redis channel {Channel}",
            RedisKeys.QuoteUpdateChannel);

        _queue.OnMessage(async message =>
        {
            var payload = (string?)message.Message ?? string.Empty;
            await _broadcaster.DispatchAsync(payload, stoppingToken).ConfigureAwait(false);
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
        {
            try
            {
                await _queue.UnsubscribeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "QuoteUpdateSubscriber failed to unsubscribe cleanly from {Channel}",
                    RedisKeys.QuoteUpdateChannel);
            }

            _queue = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
