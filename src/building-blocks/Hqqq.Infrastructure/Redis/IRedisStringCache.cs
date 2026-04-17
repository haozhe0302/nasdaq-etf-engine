namespace Hqqq.Infrastructure.Redis;

/// <summary>
/// Thin write-side seam over Redis string values. Services materializing
/// latest serving state call this instead of depending on the full
/// <c>StackExchange.Redis</c> <c>IDatabase</c> surface, which keeps writer
/// implementations tiny and makes tests trivial to assert against without
/// scraping a live Redis instance.
/// </summary>
public interface IRedisStringCache
{
    /// <summary>
    /// Set <paramref name="key"/> to <paramref name="value"/> as a Redis
    /// string. Overwrites any existing value. Intended for latest
    /// materialized serving state only — callers must not use this for raw
    /// event streams.
    /// </summary>
    Task StringSetAsync(string key, string value, CancellationToken ct);
}
