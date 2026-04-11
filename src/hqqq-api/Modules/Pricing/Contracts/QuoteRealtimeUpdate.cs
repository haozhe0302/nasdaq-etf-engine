namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Slim realtime DTO broadcast over SignalR on every quote cycle.
/// Contains only scalar fields + the latest series point (if one was
/// recorded this cycle), NOT the full chart series.
/// The full series is served once via GET /api/quote.
/// </summary>
public sealed record QuoteRealtimeUpdate
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
    public SeriesPoint? LatestSeriesPoint { get; init; }

    public required IReadOnlyList<Mover> Movers { get; init; }
    public required FreshnessInfo Freshness { get; init; }
    public required FeedInfo Feeds { get; init; }

    public string QuoteState { get; init; } = "live";
    public bool IsLive { get; init; } = true;
    public bool IsFrozen { get; init; }
    public string? PauseReason { get; init; }

    /// <summary>
    /// Build a slim realtime update from a full <see cref="QuoteSnapshot"/>
    /// (whose <see cref="QuoteSnapshot.Series"/> is ignored) and an optional
    /// series point recorded this cycle.
    /// </summary>
    public static QuoteRealtimeUpdate FromSnapshot(
        QuoteSnapshot quote,
        SeriesPoint? latestSeriesPoint)
    {
        return new QuoteRealtimeUpdate
        {
            Nav = quote.Nav,
            NavChangePct = quote.NavChangePct,
            MarketPrice = quote.MarketPrice,
            PremiumDiscountPct = quote.PremiumDiscountPct,
            Qqq = quote.Qqq,
            QqqChangePct = quote.QqqChangePct,
            BasketValueB = quote.BasketValueB,
            AsOf = quote.AsOf,
            LatestSeriesPoint = latestSeriesPoint,
            Movers = quote.Movers,
            Freshness = quote.Freshness,
            Feeds = quote.Feeds,
            QuoteState = quote.QuoteState,
            IsLive = quote.IsLive,
            IsFrozen = quote.IsFrozen,
            PauseReason = quote.PauseReason,
        };
    }
}
