using Hqqq.Infrastructure.Kafka;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Engine-wide configuration, bound from the <c>QuoteEngine</c> section in
/// <c>appsettings.json</c>. Defaults are tuned to match the legacy monolith's
/// observable behavior so B3 cut-over stays transparent to the frontend.
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

    /// <summary>
    /// File path for the lightweight engine checkpoint (basket identity +
    /// pricing basis + scale + last snapshot digest). Default is rooted in
    /// the service working directory so it's discoverable in local dev; in
    /// container deployments this should be pointed at a persistent volume
    /// via <c>QuoteEngine:CheckpointPath</c>.
    /// </summary>
    public string CheckpointPath { get; init; } = "./data/quote-engine/checkpoint.json";

    /// <summary>
    /// Cadence of periodic checkpoint writes from the materialize loop.
    /// Writes on basket activation happen out-of-band regardless of this.
    /// </summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Kafka topic carrying normalized ticks.</summary>
    public string RawTicksTopic { get; init; } = KafkaTopics.RawTicks;

    /// <summary>Kafka topic carrying the richer active-basket state event.</summary>
    public string BasketActiveTopic { get; init; } = KafkaTopics.BasketActive;
}
