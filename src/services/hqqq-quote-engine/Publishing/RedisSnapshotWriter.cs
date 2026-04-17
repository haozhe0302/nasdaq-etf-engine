using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.QuoteEngine.Abstractions;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Writes the latest <see cref="QuoteSnapshotDto"/> for a basket into Redis
/// under the namespaced <c>hqqq:snapshot:{basketId}</c> key. This is the
/// serving-layer latest-state; Kafka remains the authoritative event bus
/// via <see cref="SnapshotTopicPublisher"/>.
/// </summary>
public sealed class RedisSnapshotWriter : IQuoteSnapshotSink
{
    private readonly IRedisStringCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisSnapshotWriter(IRedisStringCache cache)
        : this(cache, HqqqJsonDefaults.Options)
    {
    }

    public RedisSnapshotWriter(IRedisStringCache cache, JsonSerializerOptions jsonOptions)
    {
        _cache = cache;
        _jsonOptions = jsonOptions;
    }

    public async Task WriteAsync(string basketId, QuoteSnapshotDto snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(basketId))
            throw new ArgumentException("basketId must be non-empty", nameof(basketId));
        ArgumentNullException.ThrowIfNull(snapshot);

        var key = RedisKeys.Snapshot(basketId);
        var payload = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await _cache.StringSetAsync(key, payload, ct).ConfigureAwait(false);
    }
}
