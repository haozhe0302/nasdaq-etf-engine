namespace Hqqq.Gateway.Services.Adapters.Aggregated;

/// <summary>
/// One-shot view of a downstream service's <c>/healthz/ready</c> response,
/// projected into the minimal shape the aggregator needs to compose the
/// gateway's <c>/api/system/health</c> payload.
/// </summary>
public sealed record ServiceHealthSnapshot
{
    public required string ServiceName { get; init; }
    public required string Status { get; init; }
    public string? Version { get; init; }
    public long? UptimeSeconds { get; init; }

    /// <summary>Per-dependency entry as reported by the downstream service.</summary>
    public required IReadOnlyList<DependencyEntry> Dependencies { get; init; }

    public required DateTimeOffset LastCheckedAtUtc { get; init; }

    /// <summary>
    /// Set when scraping the downstream failed (timeout, connection refused,
    /// non-200 response, malformed body). The aggregator translates this
    /// into a top-level <c>unknown</c> dependency status with a short
    /// reason in <c>details</c>.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Per-dependency entry as parsed from the downstream <c>/healthz/ready</c>
    /// JSON payload. <see cref="Data"/> is the structured <c>data</c> dict
    /// the service emits (e.g. ingress publishes <c>webSocketConnected</c>
    /// and <c>fallbackActive</c> there) — null when the service did not
    /// emit any structured fields for this check.
    /// </summary>
    public sealed record DependencyEntry(
        string Name,
        string Status,
        IReadOnlyDictionary<string, object?>? Data = null);
}

/// <summary>
/// Scrapes one downstream service's <c>/healthz/ready</c> endpoint within a
/// configured timeout and returns a <see cref="ServiceHealthSnapshot"/>.
/// Implementations must never throw; failures surface as
/// <see cref="ServiceHealthSnapshot.Error"/>.
/// </summary>
public interface IServiceHealthClient
{
    Task<ServiceHealthSnapshot> ProbeAsync(
        string serviceName,
        Uri baseUrl,
        CancellationToken cancellationToken);
}
