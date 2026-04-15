namespace Hqqq.Contracts.Events;

/// <summary>
/// Operational incident published to <c>ops.incidents.v1</c>.
/// Key: <see cref="Service"/>.
/// </summary>
public sealed record IncidentEventV1
{
    public required string Service { get; init; }
    public required string IncidentType { get; init; }

    /// <summary>"info", "warning", or "critical".</summary>
    public required string Severity { get; init; }

    public required string Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}
