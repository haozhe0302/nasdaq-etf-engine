using StackExchange.Redis;

namespace Hqqq.Infrastructure.Redis;

/// <summary>
/// Default <see cref="IRedisStringCache"/> implementation backed by the
/// shared <see cref="IConnectionMultiplexer"/>. <see cref="IDatabase"/> is
/// resolved per call because <c>GetDatabase()</c> is a lightweight handle —
/// the multiplexer manages the actual connection pool.
/// </summary>
public sealed class RedisStringCache : IRedisStringCache
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisStringCache(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task StringSetAsync(string key, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _multiplexer.GetDatabase();
        await db.StringSetAsync(key, value).ConfigureAwait(false);
    }
}
