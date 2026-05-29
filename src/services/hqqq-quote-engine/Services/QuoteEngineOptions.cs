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

    /// <summary>Kafka topic carrying materialized iNAV snapshot events.</summary>
    public string PricingSnapshotsTopic { get; init; } = KafkaTopics.PricingSnapshots;

    /// <summary>
    /// Cadence of the materialize loop. Matches the legacy <c>QuoteBroadcastService</c>
    /// 1 Hz tempo by default; tests lower this to keep worker-level assertions
    /// fast without losing the snapshot + delta pipeline coverage.
    /// </summary>
    public TimeSpan MaterializeInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// TTL for the per-basket calibration record persisted by
    /// <see cref="Persistence.RedisCalibrationStore"/>. After this window
    /// the bootstrap calibrator will re-run on the next QQQ tick. The
    /// 7-day default is long enough to survive routine restarts but
    /// short enough that a forgotten basket cannot indefinitely pin a
    /// stale scale across a corporate-action event.
    /// </summary>
    public TimeSpan CalibrationTtl { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Minimum fraction of basket constituents that must be priced before
    /// the bootstrap calibrator is allowed to lock a scale factor. After a
    /// restart the per-symbol store is empty and ticks arrive gradually;
    /// calibrating against a sparsely-priced basket would lock a wildly
    /// inflated scale (<c>scale = qqq / partialRaw</c>) that then survives
    /// in Redis. Requiring near-complete coverage keeps the anchored NAV
    /// close to QQQ. Range (0, 1]; default 0.90.
    /// </summary>
    public double CalibrationMinCoverage { get; init; } = 0.90;

    /// <summary>
    /// Upper bound on how long the calibrator waits for
    /// <see cref="CalibrationMinCoverage"/> before calibrating against
    /// whatever coverage is available. Prevents the engine from staying
    /// uninitialized indefinitely when a handful of illiquid constituents
    /// never tick. Measured from the first anchor tick after activation.
    /// </summary>
    public TimeSpan CalibrationMaxWait { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Drift tolerance used by the self-heal guard. Once the basket is
    /// near-complete (<see cref="CalibrationMinCoverage"/>), if the
    /// anchored NAV deviates from the live QQQ price by more than this
    /// fraction the coordinator treats the locked scale as poisoned (e.g.
    /// from a prior partial-coverage bootstrap persisted to Redis) and
    /// recalibrates. The 0.20 default sits well above any plausible
    /// premium/discount, so normal pricing is never disturbed.
    /// </summary>
    public decimal CalibrationMaxDriftPct { get; init; } = 0.20m;
}
