using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Hqqq.QuoteEngine.Abstractions;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Writes the latest <see cref="ConstituentsSnapshotDto"/> for a basket into
/// Redis under the namespaced <c>hqqq:constituents:{basketId}</c> key. Same
/// latest-state semantics as <see cref="RedisSnapshotWriter"/> — the gateway
/// reader added in B5 will deserialize this key directly.
/// </summary>
public sealed class RedisConstituentsWriter : IConstituentSnapshotSink
{
    private readonly IRedisStringCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisConstituentsWriter(IRedisStringCache cache)
        : this(cache, HqqqJsonDefaults.Options)
    {
    }

    public RedisConstituentsWriter(IRedisStringCache cache, JsonSerializerOptions jsonOptions)
    {
        _cache = cache;
        _jsonOptions = jsonOptions;
    }

    public async Task WriteAsync(string basketId, ConstituentsSnapshotDto snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(basketId))
            throw new ArgumentException("basketId must be non-empty", nameof(basketId));
        ArgumentNullException.ThrowIfNull(snapshot);

        var key = RedisKeys.Constituents(basketId);
        var payload = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await _cache.StringSetAsync(key, payload, ct).ConfigureAwait(false);
    }
}
