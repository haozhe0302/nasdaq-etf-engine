using Hqqq.Gateway.Services.Infrastructure;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IGatewayRedisReader"/> used by tests to seed the
/// Redis-backed sources without spinning up a real Redis. Supports seeded
/// string values and per-key exceptions so transport failures can be
/// exercised too.
/// </summary>
public sealed class FakeGatewayRedisReader : IGatewayRedisReader
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> _throws = new(StringComparer.Ordinal);

    public FakeGatewayRedisReader Set(string key, string? value)
    {
        _values[key] = value;
        return this;
    }

    public FakeGatewayRedisReader Throw(string key, Exception exception)
    {
        _throws[key] = exception;
        return this;
    }

    public Task<string?> StringGetAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_throws.TryGetValue(key, out var ex))
            throw ex;
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }
}
