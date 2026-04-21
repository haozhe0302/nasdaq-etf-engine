using Hqqq.Infrastructure.Redis;
using StackExchange.Redis;

namespace Hqqq.Gateway.Services.Realtime;

/// <summary>
/// Narrow seam around the Redis pub/sub mechanics used by
/// <see cref="QuoteUpdateSubscriber"/>. Exists so the subscriber can be
/// unit-tested (transient-failure + recovery posture) without standing up
/// either a real <see cref="IConnectionMultiplexer"/> or a real Redis
/// instance, and so production code keeps all StackExchange.Redis
/// mechanics in one place.
/// </summary>
public interface IRedisQuoteUpdateChannel
{
    /// <summary>
    /// Subscribes to <see cref="RedisKeys.QuoteUpdateChannel"/> and wires
    /// <paramref name="handler"/> to fire for each incoming payload. Must
    /// throw a <see cref="RedisConnectionException"/> /
    /// <see cref="RedisTimeoutException"/> / <see cref="RedisException"/>
    /// when the underlying transport is unavailable so the caller can
    /// retry with backoff.
    /// </summary>
    Task SubscribeAsync(
        Func<string, CancellationToken, Task> handler,
        CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort unsubscribe. Safe to call when no subscription is
    /// currently active.
    /// </summary>
    Task UnsubscribeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="IRedisQuoteUpdateChannel"/> implementation backed by
/// a shared <see cref="IConnectionMultiplexer"/>. All Redis-specific
/// mechanics (subscribe/unsubscribe, <c>OnMessage</c> wiring) live here;
/// <see cref="QuoteUpdateSubscriber"/> never touches StackExchange.Redis
/// types directly.
/// </summary>
public sealed class StackExchangeRedisQuoteUpdateChannel : IRedisQuoteUpdateChannel
{
    private readonly IConnectionMultiplexer _multiplexer;
    private ChannelMessageQueue? _queue;

    public StackExchangeRedisQuoteUpdateChannel(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task SubscribeAsync(
        Func<string, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var subscriber = _multiplexer.GetSubscriber();
        var channel = RedisChannel.Literal(RedisKeys.QuoteUpdateChannel);
        var queue = await subscriber.SubscribeAsync(channel).ConfigureAwait(false);

        queue.OnMessage(async message =>
        {
            var payload = (string?)message.Message ?? string.Empty;
            await handler(payload, cancellationToken).ConfigureAwait(false);
        });

        _queue = queue;
    }

    public async Task UnsubscribeAsync(CancellationToken cancellationToken)
    {
        var queue = Interlocked.Exchange(ref _queue, null);
        if (queue is null)
        {
            return;
        }

        await queue.UnsubscribeAsync().ConfigureAwait(false);
    }
}
