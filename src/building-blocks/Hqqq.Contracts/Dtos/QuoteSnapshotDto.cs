namespace Hqqq.Contracts.Dtos;

/// <summary>
/// Composite serving shape for the latest iNAV snapshot. Served as a full
/// snapshot via REST by the future gateway and used for initial page loads
/// and reconnect resync. The realtime channel broadcasts the slim
/// <see cref="QuoteUpdateDto"/> instead.
/// </summary>
public sealed record QuoteSnapshotDto
{
    public required decimal Nav { get; init; }
    public required decimal NavChangePct { get; init; }
    public required decimal MarketPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required decimal Qqq { get; init; }
    public required decimal QqqChangePct { get; init; }
    public required decimal BasketValueB { get; init; }
    public required DateTimeOffset AsOf { get; init; }

    public required IReadOnlyList<SeriesPointDto> Series { get; init; }
    public required IReadOnlyList<MoverDto> Movers { get; init; }
    public required FreshnessDto Freshness { get; init; }
    public required FeedInfoDto Feeds { get; init; }

    public required string QuoteState { get; init; }
    public required bool IsLive { get; init; }
    public required bool IsFrozen { get; init; }
    public string? PauseReason { get; init; }
}

/// <summary>
/// Slim realtime delta broadcast on every quote-engine cycle. Contains the
/// same scalars as <see cref="QuoteSnapshotDto"/> plus (optionally) the single
/// series point that was recorded this cycle — never the full series.
/// </summary>
public sealed record QuoteUpdateDto
{
    public required decimal Nav { get; init; }
    public required decimal NavChangePct { get; init; }
    public required decimal MarketPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required decimal Qqq { get; init; }
    public required decimal QqqChangePct { get; init; }
    public required decimal BasketValueB { get; init; }
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>
    /// The series point recorded this cycle, or null if the recording
    /// interval has not elapsed or we are outside market hours.
    /// </summary>
    public SeriesPointDto? LatestSeriesPoint { get; init; }

    public required IReadOnlyList<MoverDto> Movers { get; init; }
    public required FreshnessDto Freshness { get; init; }
    public required FeedInfoDto Feeds { get; init; }

    public required string QuoteState { get; init; }
    public required bool IsLive { get; init; }
    public required bool IsFrozen { get; init; }
    public string? PauseReason { get; init; }
}

/// <summary>
/// A single point in a time-series (e.g. NAV vs market chart).
/// </summary>
public sealed record SeriesPointDto
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal Market { get; init; }
}

/// <summary>
/// Top mover / laggard in the basket.
/// </summary>
public sealed record MoverDto
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required decimal ChangePct { get; init; }
    public required decimal Impact { get; init; }
    public required string Direction { get; init; }
}

/// <summary>
/// Tick freshness summary across all constituents.
/// </summary>
public sealed record FreshnessDto
{
    public required int SymbolsTotal { get; init; }
    public required int SymbolsFresh { get; init; }
    public required int SymbolsStale { get; init; }
    public required decimal FreshPct { get; init; }
    public DateTimeOffset? LastTickUtc { get; init; }
    public double? AvgTickIntervalMs { get; init; }
}

/// <summary>
/// Feed and pipeline status for the quote snapshot.
/// </summary>
public sealed record FeedInfoDto
{
    public required bool WebSocketConnected { get; init; }
    public required bool FallbackActive { get; init; }
    public required bool PricingActive { get; init; }
    public required string BasketState { get; init; }
    public required bool PendingActivationBlocked { get; init; }
    public string? PendingBlockedReason { get; init; }

    public string? MarketSessionState { get; init; }
    public bool? IsRegularSessionOpen { get; init; }
    public bool? IsTradingDay { get; init; }
    public DateTimeOffset? NextOpenUtc { get; init; }
    public string? SessionLabel { get; init; }
}
