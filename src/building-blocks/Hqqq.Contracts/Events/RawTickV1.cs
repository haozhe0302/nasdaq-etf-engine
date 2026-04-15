namespace Hqqq.Contracts.Events;

/// <summary>
/// Normalized market tick published to <c>market.raw_ticks.v1</c>.
/// Key: <see cref="Symbol"/>.
/// </summary>
public sealed record RawTickV1
{
    public required string Symbol { get; init; }
    public required decimal Last { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
    public required string Currency { get; init; }
    public required string Provider { get; init; }
    public required DateTimeOffset ProviderTimestamp { get; init; }
    public required DateTimeOffset IngressTimestamp { get; init; }
    public required long Sequence { get; init; }
}
