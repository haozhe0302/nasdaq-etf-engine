namespace Hqqq.ReferenceData.Configuration;

/// <summary>
/// Strongly-typed configuration surface for <c>hqqq-reference-data</c>. Bound
/// from the <c>ReferenceData</c> config section. Owns the knobs the service
/// needs to run the basket pipeline: live-holdings source selection, refresh
/// cadence, validation policy, publish topic override, publish-health
/// thresholds, and Phase-2-native corporate-action adjustment. All magic
/// numbers live here so nothing is scattered across the code base.
/// </summary>
public sealed class ReferenceDataOptions
{
    public const string SectionName = "ReferenceData";

    public LiveHoldingsOptions LiveHoldings { get; set; } = new();
    public RefreshOptions Refresh { get; set; } = new();
    public ValidationOptions Validation { get; set; } = new();
    public PublishOptions Publish { get; set; } = new();
    public PublishHealthOptions PublishHealth { get; set; } = new();
    public CorporateActionOptions CorporateActions { get; set; } = new();

    /// <summary>
    /// Real-source basket pipeline ported from the Phase 1 Basket module.
    /// <see cref="BasketOptions.Mode"/> selects between the ported
    /// <c>RealSource</c> posture (production default: AlphaVantage + Nasdaq
    /// JSON adapters with 08:00 / 08:30 / 09:30 ET lifecycle and market-
    /// open activation) and <c>Seed</c> (deterministic fallback for
    /// local/offline/dev). In Production, <c>Seed</c> requires the
    /// explicit <see cref="BasketOptions.AllowDeterministicSeedInProduction"/>
    /// override or startup fails loudly.
    /// </summary>
    public BasketOptions Basket { get; set; } = new();

    /// <summary>
    /// Optional override for the deterministic fallback seed path. When set
    /// and the file exists, the loader reads it instead of the embedded
    /// resource. Useful when iterating on demo content without rebuilding
    /// the image.
    /// </summary>
    public string? SeedPath { get; set; }
}

/// <summary>
/// Production-grade basket pipeline configuration. Mirrors the Phase 1
/// Basket module (<c>src/hqqq-api/Modules/Basket</c>) lifecycle and
/// sources but lives natively in <c>hqqq-reference-data</c> with no
/// runtime dependency on the monolith.
/// </summary>
/// <remarks>
/// <para>
/// JSON adapters only. The Phase 1 HTML scrapers (StockAnalysis, Schwab)
/// are intentionally NOT ported as Phase 2 production sources — they
/// would bring an HtmlAgilityPack dependency and live scraping risk to
/// the deployment gate. <see cref="IBasketSourceAdapter"/> keeps the
/// shape required to re-introduce them later if ever needed.
/// </para>
/// </remarks>
public sealed class BasketOptions
{
    /// <summary>
    /// Selects the basket pipeline posture. <see cref="BasketMode.RealSource"/>
    /// is the Production default and runs the ported lifecycle
    /// (fetch → merge → candidate → activate at market open).
    /// <see cref="BasketMode.Seed"/> is the explicit local/offline
    /// posture; in Production it fails startup unless
    /// <see cref="AllowDeterministicSeedInProduction"/> is true.
    /// </summary>
    public BasketMode Mode { get; set; } = BasketMode.RealSource;

    /// <summary>
    /// Operator acknowledgement that running Production on deterministic
    /// seed fallback is an accepted risk. Only honored when
    /// <c>ASPNETCORE_ENVIRONMENT=Production</c>. Must be set explicitly;
    /// there is no implicit production-seed path.
    /// </summary>
    public bool AllowDeterministicSeedInProduction { get; set; } = false;

    /// <summary>IANA or Windows zone id for market-hours calculations. Default <c>America/New_York</c>.</summary>
    public string MarketTimeZone { get; set; } = "America/New_York";

    public BasketSourcesOptions Sources { get; set; } = new();
    public BasketScheduleOptions Schedule { get; set; } = new();
    public BasketCacheOptions Cache { get; set; } = new();
}

public enum BasketMode
{
    /// <summary>Run the ported Phase 1 basket pipeline against real JSON upstreams.</summary>
    RealSource = 0,
    /// <summary>Local/offline posture — composite fall-through to the deterministic seed.</summary>
    Seed = 1,
}

/// <summary>
/// Per-adapter configuration for the ported basket pipeline. Disabling
/// every adapter in Production while also keeping <see cref="BasketOptions.Mode"/>
/// on <see cref="BasketMode.RealSource"/> is treated the same as running
/// on the seed and is rejected by the startup guard.
/// </summary>
public sealed class BasketSourcesOptions
{
    public AlphaVantageSourceOptions AlphaVantage { get; set; } = new();
    public NasdaqSourceOptions Nasdaq { get; set; } = new();
}

public sealed class AlphaVantageSourceOptions
{
    /// <summary>Opt-in toggle. Disabled by default because it requires an API key.</summary>
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class NasdaqSourceOptions
{
    /// <summary>
    /// Nasdaq public list-type endpoint. No API key required, but subject
    /// to occasional rate limits and schema drift; the adapter tolerates
    /// both gracefully and falls back to the raw-source cache.
    /// </summary>
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "https://api.nasdaq.com/api/quote/list-type/nasdaq100";
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// 08:00 / 08:30 / 09:30 ET lifecycle — mirrors the Phase 1 scheduler
/// (<c>BasketRefreshService</c>). Times are parsed as local wall-clock in
/// <see cref="BasketOptions.MarketTimeZone"/>.
/// </summary>
public sealed class BasketScheduleOptions
{
    /// <summary>Fetch raw sources into the per-source cache (default 08:00 local).</summary>
    public string FetchTimeLocal { get; set; } = "08:00";

    /// <summary>Merge cached sources into a candidate basket (default 08:30 local).</summary>
    public string MergeTimeLocal { get; set; } = "08:30";

    /// <summary>Promote pending → active iff the market is open (default 09:30 local).</summary>
    public string ActivateTimeLocal { get; set; } = "09:30";
}

/// <summary>
/// On-disk cache paths mirroring Phase 1's <c>RawSourceCacheService</c>
/// and <c>BasketCacheService</c>. Opt-in; when the directory is absent
/// the pipeline degrades to in-memory only.
/// </summary>
public sealed class BasketCacheOptions
{
    /// <summary>Per-source raw JSON cache directory (0 disables on-disk caching).</summary>
    public string RawCacheDir { get; set; } = "data/raw";

    /// <summary>Last-good merged snapshot file path.</summary>
    public string MergedCacheFilePath { get; set; } = "data/basket-cache.json";
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

/// <summary>
/// Phase-2-native corporate-action adjustment configuration. The composite
/// provider tries the <see cref="File"/> source first (deterministic,
/// offline-safe), then optionally overlays <see cref="Tiingo"/>-derived
/// splits when enabled. Supported scope is narrow and explicit: forward /
/// reverse stock splits, ticker renames, constituent add/remove detection,
/// and scale-factor continuity across basket transitions. Dividends,
/// spin-offs, mergers, and cross-exchange moves are out of scope.
/// </summary>
public sealed class CorporateActionOptions
{
    public FileCorporateActionOptions File { get; set; } = new();
    public TiingoCorporateActionOptions Tiingo { get; set; } = new();

    /// <summary>
    /// How far back the adjustment window looks when computing cumulative
    /// split factors. The window runs from <c>snapshot.AsOfDate + 1</c>
    /// through the runtime date; <see cref="LookbackDays"/> is an upper
    /// bound applied when the as-of is unexpectedly ancient.
    /// </summary>
    public int LookbackDays { get; set; } = 365;

    /// <summary>
    /// Operator acknowledgement that running Production with only the
    /// offline/file corp-action provider is an accepted risk (i.e. no
    /// Tiingo overlay). Only honored when
    /// <c>ASPNETCORE_ENVIRONMENT=Production</c>; when false and Tiingo is
    /// disabled, startup fails loudly. Default false so silent
    /// offline-only production boots are impossible.
    /// </summary>
    public bool AllowOfflineOnlyInProduction { get; set; } = false;
}

public sealed class FileCorporateActionOptions
{
    /// <summary>
    /// Optional filesystem path to a JSON corp-action drop. When set and
    /// readable, overrides the embedded <c>Resources/corporate-actions-seed.json</c>.
    /// </summary>
    public string? Path { get; set; }
}

public sealed class TiingoCorporateActionOptions
{
    /// <summary>When <c>true</c>, the composite provider overlays Tiingo EOD splits on top of the file source.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Tiingo API key. Reference-data has its own knob so ingress + refdata can use different keys if needed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Tiingo EOD base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.tiingo.com/tiingo/daily";

    /// <summary>Per-request timeout for Tiingo EOD calls.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Maximum concurrent per-symbol Tiingo requests.</summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>Per-symbol cache TTL before re-fetching from Tiingo.</summary>
    public int CacheTtlMinutes { get; set; } = 60;
}
