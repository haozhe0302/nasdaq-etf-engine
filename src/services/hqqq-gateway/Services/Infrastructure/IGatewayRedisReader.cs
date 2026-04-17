namespace Hqqq.Gateway.Services.Infrastructure;

/// <summary>
/// Minimal read-side seam over Redis string values used by the gateway's
/// Redis-backed sources. Kept gateway-local on purpose: the shared
/// <c>Hqqq.Infrastructure.Redis.IRedisStringCache</c> is intentionally
/// write-only (writers in <c>hqqq-quote-engine</c>) and widening it would
/// be a cross-project refactor outside the scope of B5. A tiny
/// gateway-owned seam also keeps unit tests free of a live Redis.
/// </summary>
public interface IGatewayRedisReader
{
    /// <summary>
    /// Returns the string value at <paramref name="key"/>, or <c>null</c>
    /// when the key does not exist or is empty. Implementations must not
    /// swallow transport errors silently — let them bubble so the caller
    /// can translate them into a controlled degraded response.
    /// </summary>
    Task<string?> StringGetAsync(string key, CancellationToken ct);
}
