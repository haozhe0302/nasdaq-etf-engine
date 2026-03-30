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
}
