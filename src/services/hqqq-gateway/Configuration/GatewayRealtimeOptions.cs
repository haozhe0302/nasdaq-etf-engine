namespace Hqqq.Gateway.Configuration;

/// <summary>
/// Bound from <c>Gateway:Realtime</c>. Controls whether the gateway wires
/// up the Redis pub/sub &rarr; SignalR bridge (<c>QuoteUpdateSubscriber</c>)
/// and how it retries when Redis is transiently unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Default posture is <see cref="Enabled"/> = <c>true</c>: the gateway is
/// the realtime fan-out for every connected UI, so normal runtime and
/// production must run with realtime on. Tests and offline smoke runs that
/// do not exercise pub/sub explicitly set <c>Gateway:Realtime:Enabled=false</c>
/// so they do not accidentally depend on a real Redis on
/// <c>localhost:6379</c>.
/// </para>
/// <para>
/// Even when enabled the subscriber never fails the host: on Redis
/// unavailability the subscriber logs a warning and retries with bounded
/// exponential backoff (<see cref="InitialRetryDelayMs"/> capped at
/// <see cref="MaxRetryDelayMs"/>, with jitter), so the host stays up and
/// <c>/api/system/health</c> remains servable. This is the Phase 2
/// "degraded-not-crashed" contract.
/// </para>
/// </remarks>
public sealed class GatewayRealtimeOptions
{
    public const string SectionName = "Gateway:Realtime";

    /// <summary>
    /// When <c>true</c> the gateway registers <c>QuoteUpdateSubscriber</c>
    /// and bridges Redis pub/sub to SignalR. When <c>false</c> no Redis
    /// subscription is attempted at all. Default <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Initial retry delay, in milliseconds, between failed subscribe
    /// attempts. Doubles up to <see cref="MaxRetryDelayMs"/> with a small
    /// random jitter. Default 1000ms.
    /// </summary>
    public int InitialRetryDelayMs { get; set; } = 1_000;

    /// <summary>
    /// Upper bound for the retry delay, in milliseconds. Default 30000ms.
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = 30_000;
}
