using StackExchange.Redis;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Default <see cref="IRedisChannelPublisher"/> backed by a shared
/// <see cref="IConnectionMultiplexer"/>. Resolves a fresh subscriber per call
/// (cheap; the multiplexer caches it internally) and forwards the payload to
/// the named channel.
/// </summary>
public sealed class StackExchangeRedisChannelPublisher : IRedisChannelPublisher
{
    private readonly IConnectionMultiplexer _multiplexer;

    public StackExchangeRedisChannelPublisher(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task PublishAsync(string channel, string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("channel must be non-empty", nameof(channel));
        ArgumentNullException.ThrowIfNull(payload);

        ct.ThrowIfCancellationRequested();

        var subscriber = _multiplexer.GetSubscriber();
        await subscriber
            .PublishAsync(RedisChannel.Literal(channel), payload, CommandFlags.None)
            .ConfigureAwait(false);
    }
}
