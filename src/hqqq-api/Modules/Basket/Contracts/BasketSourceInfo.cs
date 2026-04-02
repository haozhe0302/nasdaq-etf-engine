namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Describes the provenance and quality of a <see cref="BasketSnapshot"/>.
/// </summary>
public sealed record BasketSourceInfo
{
    public required string SourceName { get; init; }
    public required string SourceType { get; init; }
    public required bool IsDegraded { get; init; }
    public required DateOnly SourceAsOfDate { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required DateTimeOffset CacheWrittenAtUtc { get; init; }
    public TimeSpan? CacheAge { get; init; }
    public required bool OfficialWeightsAvailable { get; init; }
    public required bool OfficialSharesAvailable { get; init; }
}

/// <summary>
/// Tracks per-source fetch outcome for a single refresh cycle.
/// </summary>
public sealed record SourceFetchOutcome
{
    public required string SourceName { get; init; }
    public required bool Success { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public int RowCount { get; init; }
    public string? Error { get; init; }
    public DateOnly? SnapshotDate { get; init; }
}
