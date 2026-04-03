namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Describes the provenance and quality of a <see cref="BasketSnapshot"/>.
/// </summary>
public sealed record BasketSourceInfo
{
    public required string SourceName { get; init; }

    /// <summary>"primary", "cache", or "degraded-fallback".</summary>
    public required string SourceType { get; init; }

    public required bool IsDegraded { get; init; }
    public required DateOnly SourceAsOfDate { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required DateTimeOffset CacheWrittenAtUtc { get; init; }
    public TimeSpan? CacheAge { get; init; }

    /// <summary>True only when the anchor block carries disclosed ETF weights. False for full basket.</summary>
    public required bool OfficialWeightsAvailable { get; init; }

    /// <summary>True only when the anchor block carries disclosed ETF shares. False for full basket.</summary>
    public required bool OfficialSharesAvailable { get; init; }

    /// <summary>True when the tail block uses proxy-derived or fallback weights.</summary>
    public bool HasProxyTail { get; init; }

    /// <summary>True when any source result was loaded from a raw-source cache instead of a live fetch.</summary>
    public bool UsedRawSourceCache { get; init; }

    /// <summary>"hybrid", "degraded", "cache", or "unknown".</summary>
    public string BasketMode { get; init; } = "unknown";
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

    /// <summary>"live" if fetched from the remote source; "raw-cache" if loaded from a prior successful raw cache.</summary>
    public string Origin { get; init; } = "live";
}
