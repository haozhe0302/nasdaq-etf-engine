namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// Engine-internal shape of a single market tick. Insulates the pipeline
/// from the wire-level <c>RawTickV1</c> event so that unit tests can drive
/// the engine without any Kafka / contracts coupling at the call-site.
/// </summary>
public sealed record NormalizedTick
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

    /// <summary>
    /// Optional previous-close hint carried on bootstrap ticks so the engine
    /// can populate %-change without a separate anchors feed in B2.
    /// </summary>
    public decimal? PreviousClose { get; init; }
}
