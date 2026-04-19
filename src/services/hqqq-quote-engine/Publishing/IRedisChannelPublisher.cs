namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Thin project-local seam over <c>StackExchange.Redis.ISubscriber.PublishAsync</c>
/// so <see cref="RedisQuoteUpdatePublisher"/> stays unit-testable without a
/// live multiplexer. Intentionally not promoted to the shared infrastructure
/// project — every other Redis pub/sub use case in this repo (gateway
/// subscriber) uses <c>IConnectionMultiplexer.GetSubscriber()</c> directly.
/// </summary>
public interface IRedisChannelPublisher
{
    Task PublishAsync(string channel, string payload, CancellationToken ct);
}
