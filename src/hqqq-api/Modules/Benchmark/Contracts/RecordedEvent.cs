namespace Hqqq.Api.Modules.Benchmark.Contracts;

/// <summary>
/// A single recorded event in the JSONL benchmark stream.
/// Uses a flat shape with nullable fields keyed by <see cref="EventType"/>
/// for simple JSONL serialization without polymorphic converters.
/// </summary>
public sealed record RecordedEvent
{
    // ── discriminator ────────────────────────────────────

    /// <summary>"tick", "transport", "quote", or "activation".</summary>
    public required string EventType { get; init; }

    /// <summary>UTC timestamp when the event was recorded.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    // ── tick fields ──────────────────────────────────────

    public string? Symbol { get; init; }
    public decimal? Price { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? UpstreamTimestamp { get; init; }

    // ── transport fields ─────────────────────────────────

    /// <summary>
    /// "fallback_activated" or "ws_recovered".
    /// </summary>
    public string? Action { get; init; }

    // ── quote fields ─────────────────────────────────────

    public decimal? Nav { get; init; }
    public decimal? MarketPrice { get; init; }
    public decimal? PremiumDiscountBps { get; init; }
    public int? SymbolsTotal { get; init; }
    public int? SymbolsStale { get; init; }
    public double? BroadcastMs { get; init; }
    public double? TickToQuoteMs { get; init; }

    // ── activation fields ────────────────────────────────

    public string? Fingerprint { get; init; }
    public double? JumpBps { get; init; }
}
