namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Engine-wide constants. Not yet exposed via <c>appsettings.json</c> in
/// B2 to keep the diff narrow — wire up IOptions in B3 alongside the real
/// Kafka consumers.
/// </summary>
public sealed class QuoteEngineOptions
{
    /// <summary>
    /// A per-symbol quote older than this is considered stale. Matches the
    /// legacy <c>TiingoOptions.StaleAfterSeconds</c> default (30s).
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cadence at which the materializer records a new point into the
    /// series ring buffer. Matches the legacy
    /// <c>PricingOptions.SeriesRecordIntervalMs</c> default (5s).
    /// </summary>
    public TimeSpan SeriesRecordInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Anchor symbol for market-price / change-percent calculations.
    /// </summary>
    public string AnchorSymbol { get; init; } = "QQQ";

    public int SeriesCapacity { get; init; } = 4096;

    public int MoversTopN { get; init; } = 5;
}
