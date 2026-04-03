namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Frontend-ready aggregated quote snapshot for the Market page.
/// Broadcast via SignalR every second and served from GET /api/quote.
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
}
