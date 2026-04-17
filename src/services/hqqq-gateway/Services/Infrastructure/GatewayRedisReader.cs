using StackExchange.Redis;

namespace Hqqq.Gateway.Services.Infrastructure;

/// <summary>
/// Default <see cref="IGatewayRedisReader"/> backed by the shared
/// <see cref="IConnectionMultiplexer"/>. <c>GetDatabase()</c> is a cheap
/// handle so resolving it per call is fine.
/// </summary>
public sealed class GatewayRedisReader : IGatewayRedisReader
{
    private readonly IConnectionMultiplexer _multiplexer;

    public GatewayRedisReader(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task<string?> StringGetAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _multiplexer.GetDatabase();
        var value = await db.StringGetAsync(key).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : value.ToString();
    }
}
