using System.Collections.Concurrent;
using Hqqq.Infrastructure.Redis;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRedisStringCache"/> that records every write.
/// Tests inspect <see cref="Writes"/> / <see cref="Values"/> directly rather
/// than scraping a live Redis — the point of the cache seam is exactly this
/// kind of deterministic assertion.
/// </summary>
public sealed class InMemoryRedisStringCache : IRedisStringCache
{
    private readonly ConcurrentQueue<(string Key, string Value)> _writes = new();
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<(string Key, string Value)> Writes => _writes;
    public IReadOnlyDictionary<string, string> Values => _values;

    public Task StringSetAsync(string key, string value, CancellationToken ct)
    {
        _writes.Enqueue((key, value));
        _values[key] = value;
        return Task.CompletedTask;
    }
}
