namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Frontend-ready aggregated quote snapshot for the Market page.
/// Served as a full snapshot via GET /api/quote for initial page load
/// and reconnect resync. NOT broadcast directly over SignalR — the
/// realtime channel sends the slim <see cref="QuoteRealtimeUpdate"/> instead.
/// </summary>
public sealed record QuoteSnapshot
{
    public required decimal Nav { get; init; }
    public required decimal NavChangePct { get; init; }
    public required decimal MarketPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required decimal Qqq { get; init; }
    public required decimal BasketValueB { get; init; }
    public required DateTimeOffset AsOf { get; init; }
    public required IReadOnlyList<SeriesPoint> Series { get; init; }
    public required IReadOnlyList<Mover> Movers { get; init; }
    public required FreshnessInfo Freshness { get; init; }
    public required FeedInfo Feeds { get; init; }

    public string QuoteState { get; init; } = "live";
    public bool IsLive { get; init; } = true;
    public bool IsFrozen { get; init; }
    public string? PauseReason { get; init; }
}
