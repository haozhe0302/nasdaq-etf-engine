using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.Observability.Identity;

namespace Hqqq.Observability.Health;

/// <summary>
/// Builds the gateway-facing <c>/api/system/health</c> payload in the shape
/// the existing frontend <c>BSystemHealth</c> adapter expects:
/// <c>{ serviceName, status, checkedAtUtc, version, runtime, metrics?,
/// upstream?, dependencies[] }</c>.
/// Only used by the gateway's aggregator; downstream services emit the
/// <see cref="HealthzPayloadBuilder"/> shape.
/// </summary>
public static class SystemHealthPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Allowed status vocabulary for top-level/dependency status fields,
    /// matching the frontend's <c>toHealthStatus</c> mapping.
    /// </summary>
    public static class Status
    {
        public const string Healthy = "healthy";
        public const string Degraded = "degraded";
        public const string Unhealthy = "unhealthy";
        public const string Unknown = "unknown";
        public const string Idle = "idle";
    }

    /// <summary>
    /// Aggregator-friendly view of one downstream dependency (a service or
    /// an infrastructure component) that the gateway includes in the
    /// system-health payload.
    /// </summary>
    public sealed record DependencyEntry(
        string Name,
        string Status,
        DateTimeOffset LastCheckedAtUtc,
        string? Details);

    /// <summary>
    /// Operator-facing transport diagnostics for the system-health
    /// <c>upstream</c> block. Mirrors the wire shape the legacy monolith
    /// emitted (<c>Hqqq.Api.Modules.System.Contracts.UpstreamDiagnostics</c>)
    /// so the existing frontend adapter keeps rendering without change.
    /// </summary>
    public sealed record UpstreamView(
        bool WebSocketConnected,
        bool FallbackActive,
        string? LastUpstreamError = null,
        DateTimeOffset? LastUpstreamErrorAtUtc = null,
        DateTimeOffset? LastPublishedTickUtc = null);

    /// <summary>
    /// Composes the full <c>BSystemHealth</c>-shaped payload.
    /// <paramref name="topLevelStatus"/> is computed by the caller using
    /// <see cref="RollupStatus"/> so the rule lives in one place.
    /// <paramref name="upstream"/> populates the system-health <c>upstream</c>
    /// block surfaced to the frontend /system page; pass <c>null</c> to keep
    /// the previous behaviour of omitting the block entirely.
    /// <paramref name="metrics"/> populates the system-health <c>metrics</c>
    /// block the frontend Runtime Metrics panel renders; pass <c>null</c> when
    /// the aggregator has no snapshot to derive runtime metrics from (the
    /// panel then shows its "waiting for metrics" placeholder).
    /// </summary>
    public static string Build(
        ServiceIdentity identity,
        string topLevelStatus,
        IReadOnlyList<DependencyEntry> dependencies,
        UpstreamView? upstream = null,
        object? metrics = null)
    {
        var runtime = RuntimeInfo.Capture(identity);
        var payload = new
        {
            serviceName = identity.ServiceName,
            status = topLevelStatus,
            checkedAtUtc = DateTimeOffset.UtcNow,
            version = identity.ServiceVersion,
            sourceMode = "aggregated",
            runtime = new
            {
                uptimeSeconds = runtime.UptimeSeconds,
                memoryMb = runtime.MemoryMb,
                gcGen0 = runtime.GcGen0,
                gcGen1 = runtime.GcGen1,
                gcGen2 = runtime.GcGen2,
                threadCount = runtime.ThreadCount,
            },
            metrics,
            upstream = upstream is null
                ? null
                : new
                {
                    webSocketConnected = upstream.WebSocketConnected,
                    fallbackActive = upstream.FallbackActive,
                    lastUpstreamError = upstream.LastUpstreamError,
                    lastUpstreamErrorAtUtc = upstream.LastUpstreamErrorAtUtc,
                    lastPublishedTickUtc = upstream.LastPublishedTickUtc,
                },
            dependencies = dependencies
                .Select(d => new
                {
                    name = d.Name,
                    status = d.Status,
                    lastCheckedAtUtc = d.LastCheckedAtUtc,
                    details = d.Details,
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Aggregates a list of dependency statuses into a top-level status.
    /// We deliberately collapse <c>unhealthy</c> at the top level into
    /// <c>degraded</c> so a single missing worker doesn't trip frontend
    /// alarms or cause the System page to render as fully down.
    /// <c>unknown</c> and <c>idle</c> dependencies are never escalated.
    /// </summary>
    public static string RollupStatus(IEnumerable<string> dependencyStatuses)
    {
        var anyDegradedOrWorse = false;
        foreach (var s in dependencyStatuses)
        {
            if (s == Status.Unhealthy || s == Status.Degraded)
                anyDegradedOrWorse = true;
        }
        return anyDegradedOrWorse ? Status.Degraded : Status.Healthy;
    }

    internal static JsonSerializerOptions Options => JsonOptions;
}
