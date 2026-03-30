namespace Hqqq.Api.Modules.System.Contracts;

/// <summary>
/// Health status of a single downstream dependency (database, cache, broker, etc.).
/// </summary>
public sealed record DependencyHealth
{
    /// <summary>Dependency name (e.g. "postgresql", "redis", "kafka").</summary>
    public required string Name { get; init; }

    /// <summary>Status (e.g. "healthy", "unhealthy", "unknown").</summary>
    public required string Status { get; init; }

    /// <summary>UTC timestamp of the last health probe.</summary>
    public required DateTimeOffset LastCheckedAtUtc { get; init; }

    /// <summary>Optional diagnostic details or error message.</summary>
    public string? Details { get; init; }
}
