namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Database-shape representation of a single row in the <c>raw_ticks</c>
/// hypertable. Both timestamps are always normalized to UTC at the mapping
/// boundary so downstream SQL never sees a local-time
/// <see cref="DateTimeOffset"/>.
/// </summary>
public sealed record RawTickRow
{
    public required string Symbol { get; init; }
    public required DateTimeOffset ProviderTimestamp { get; init; }
    public required DateTimeOffset IngressTimestamp { get; init; }
    public required decimal Last { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
    public required string Currency { get; init; }
    public required string Provider { get; init; }
    public required long Sequence { get; init; }
}
