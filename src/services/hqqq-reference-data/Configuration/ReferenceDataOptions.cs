namespace Hqqq.ReferenceData.Configuration;

/// <summary>
/// Strongly-typed configuration surface for <c>hqqq-reference-data</c>. Bound
/// from the <c>ReferenceData</c> config section. Owns the four knobs the
/// service needs to run the basket pipeline in either operating mode:
/// live-holdings source selection, refresh cadence, validation policy, and
/// publish topic override. All magic numbers live here so nothing is
/// scattered across the code base.
/// </summary>
public sealed class ReferenceDataOptions
{
    public const string SectionName = "ReferenceData";

    public LiveHoldingsOptions LiveHoldings { get; set; } = new();
    public RefreshOptions Refresh { get; set; } = new();
    public ValidationOptions Validation { get; set; } = new();
    public PublishOptions Publish { get; set; } = new();
    public PublishHealthOptions PublishHealth { get; set; } = new();

    /// <summary>
    /// Optional override for the deterministic fallback seed path. When set
    /// and the file exists, the loader reads it instead of the embedded
    /// resource. Useful when iterating on demo content without rebuilding
    /// the image.
    /// </summary>
    public string? SeedPath { get; set; }
}

/// <summary>
/// Live-holdings source selector. <see cref="HoldingsSourceType.None"/> is
/// the default — composite goes straight to the deterministic fallback seed
/// (the expected interview-demo posture). <see cref="HoldingsSourceType.File"/>
/// reads a JSON drop at <see cref="FilePath"/>; <see cref="HoldingsSourceType.Http"/>
/// GETs <see cref="HttpUrl"/>. The JSON shape matches the committed seed so
/// provider-specific adapters can plug in behind <c>IHoldingsSource</c> later.
/// </summary>
public sealed class LiveHoldingsOptions
{
    public HoldingsSourceType SourceType { get; set; } = HoldingsSourceType.None;

    /// <summary>Filesystem path consulted when <see cref="SourceType"/> = File.</summary>
    public string? FilePath { get; set; }

    /// <summary>HTTP URL consulted when <see cref="SourceType"/> = Http.</summary>
    public string? HttpUrl { get; set; }

    /// <summary>Per-request timeout for the HTTP live source.</summary>
    public int HttpTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// If set, a live snapshot whose <c>asOfDate</c> is older than this many
    /// hours is treated as stale and the composite falls back to the seed.
    /// 0 disables the staleness check.
    /// </summary>
    public int StaleAfterHours { get; set; } = 0;
}

public enum HoldingsSourceType
{
    None = 0,
    File = 1,
    Http = 2,
}

public sealed class RefreshOptions
{
    /// <summary>Periodic refresh cadence. 0 disables the timer (startup-only refresh).</summary>
    public int IntervalSeconds { get; set; } = 600;

    /// <summary>
    /// Slow republish cadence. On tick, the current active basket is
    /// re-published onto the compacted topic even when nothing changed, so
    /// late / restarted consumers hydrate without operator action.
    /// </summary>
    public int RepublishIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Upper bound on the startup refresh attempt. The pipeline always
    /// activates <i>something</i> (live OR seed) before this deadline so the
    /// readiness probe can flip green.
    /// </summary>
    public int StartupMaxWaitSeconds { get; set; } = 30;
}

public sealed class ValidationOptions
{
    /// <summary>
    /// In strict mode, any live-source validation failure causes a fall-back
    /// to the seed (instead of quietly accepting partially-broken input).
    /// In permissive mode, the snapshot is still accepted if the minimum
    /// structural invariants (non-empty, no duplicates) hold.
    /// </summary>
    public bool Strict { get; set; } = true;

    /// <summary>
    /// Soft lower bound on constituent count. The Nasdaq-100 universe is
    /// ~100 names but can drift to 99/100/101 depending on index events;
    /// a count below this threshold is treated as a clearly broken feed.
    /// </summary>
    public int MinConstituents { get; set; } = 50;

    /// <summary>Soft upper bound on constituent count; guards against duplicated feeds.</summary>
    public int MaxConstituents { get; set; } = 150;
}

public sealed class PublishOptions
{
    /// <summary>
    /// Optional topic override. Leaving this null uses
    /// <c>Hqqq.Infrastructure.Kafka.KafkaTopics.BasketActive</c> (the
    /// repo-canonical name <c>refdata.basket.active.v1</c>).
    /// </summary>
    public string? TopicName { get; set; }
}

/// <summary>
/// Thresholds driving the <c>/healthz/ready</c> state machine for the
/// active-basket publisher. Readiness degrades to <c>Degraded</c> after
/// <see cref="DegradedAfterConsecutiveFailures"/> back-to-back publish
/// failures, and to <c>Unhealthy</c> after
/// <see cref="UnhealthyAfterConsecutiveFailures"/> or when no successful
/// publish has happened for longer than <see cref="MaxSilenceSeconds"/>.
/// Before the very first successful publish, a grace window of
/// <see cref="FirstActivationGraceSeconds"/> applies so a slow-but-working
/// broker doesn't trip readiness on cold start.
/// </summary>
public sealed class PublishHealthOptions
{
    /// <summary>
    /// How long after activation to stay <c>Degraded</c> (not <c>Unhealthy</c>)
    /// when the first publish has not yet succeeded. Covers cold-start
    /// timing against a slow broker. 0 disables the grace window.
    /// </summary>
    public int FirstActivationGraceSeconds { get; set; } = 60;

    /// <summary>
    /// Number of consecutive publish failures before readiness flips
    /// from <c>Healthy</c> to <c>Degraded</c>. One failure is already
    /// noteworthy for a compacted basket topic.
    /// </summary>
    public int DegradedAfterConsecutiveFailures { get; set; } = 1;

    /// <summary>
    /// Number of consecutive publish failures before readiness flips
    /// from <c>Degraded</c> to <c>Unhealthy</c> (503).
    /// </summary>
    public int UnhealthyAfterConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Maximum tolerated silence between successful publishes. Guards
    /// against a slow/hung broker where attempts are "succeeding" but
    /// the effective cadence is unacceptable for downstream consumers.
    /// </summary>
    public int MaxSilenceSeconds { get; set; } = 900;
}
