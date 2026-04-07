using System.Diagnostics;

namespace Hqqq.Api.Modules.System.Contracts;

/// <summary>
/// Health status of the HQQQ engine and its downstream dependencies.
/// </summary>
public sealed record SystemHealth
{
    /// <summary>Name of the service (e.g. "hqqq-api").</summary>
    public required string ServiceName { get; init; }

    /// <summary>Aggregate health status (e.g. "healthy", "degraded", "unhealthy").</summary>
    public required string Status { get; init; }

    /// <summary>UTC timestamp when the check was performed.</summary>
    public required DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>Application version.</summary>
    public required string Version { get; init; }

    /// <summary>Health of individual downstream dependencies.</summary>
    public required IReadOnlyList<DependencyHealth> Dependencies { get; init; }

    public required RuntimeInfo Runtime { get; init; }

    /// <summary>Live observability metrics snapshot (gauges, percentiles, counters).</summary>
    public RuntimeMetricsSnapshot? Metrics { get; init; }

    /// <summary>Upstream Tiingo WebSocket transport diagnostics.</summary>
    public UpstreamDiagnostics? Upstream { get; init; }
}

/// <summary>
/// Diagnostics for the upstream Tiingo WebSocket connection,
/// surfacing transport state and the most recent upstream error.
/// </summary>
public sealed record UpstreamDiagnostics
{
    public required bool WebSocketConnected { get; init; }
    public required bool FallbackActive { get; init; }
    public string? LastUpstreamError { get; init; }
    public int? LastUpstreamErrorCode { get; init; }
    public DateTimeOffset? LastUpstreamErrorAtUtc { get; init; }
}

public sealed record RuntimeInfo
{
    public required long UptimeSeconds { get; init; }
    public required long MemoryMb { get; init; }
    public required int GcGen0 { get; init; }
    public required int GcGen1 { get; init; }
    public required int GcGen2 { get; init; }
    public required int ThreadCount { get; init; }

    public static RuntimeInfo Capture()
    {
        var proc = Process.GetCurrentProcess();
        return new RuntimeInfo
        {
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds,
            MemoryMb = proc.WorkingSet64 / (1024 * 1024),
            GcGen0 = GC.CollectionCount(0),
            GcGen1 = GC.CollectionCount(1),
            GcGen2 = GC.CollectionCount(2),
            ThreadCount = proc.Threads.Count,
        };
    }
}
