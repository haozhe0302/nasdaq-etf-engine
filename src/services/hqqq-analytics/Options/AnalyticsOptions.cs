namespace Hqqq.Analytics.Options;

/// <summary>
/// Analytics-only knobs bound from the "Analytics" configuration section.
/// Kafka/Timescale connection details live in shared infrastructure options
/// and are intentionally not duplicated here.
/// </summary>
/// <remarks>
/// C4 scope is a report/replay <em>skeleton</em>. Only <see cref="ReportMode"/>
/// is implemented today; <c>replay</c>, <c>anomaly</c>, and <c>backfill</c>
/// modes are reserved so downstream phases can plug in without reshaping the
/// options surface.
/// </remarks>
public sealed class AnalyticsOptions
{
    /// <summary>
    /// Canonical string for the only mode implemented in C4.
    /// </summary>
    public const string ReportMode = "report";

    /// <summary>
    /// One of <c>report</c>, <c>replay</c>, <c>anomaly</c>, <c>backfill</c>.
    /// Only <c>report</c> is implemented in C4; others throw a clear
    /// "not implemented in C4" error when dispatched.
    /// </summary>
    public string Mode { get; set; } = ReportMode;

    /// <summary>
    /// Basket identifier whose persisted snapshots the report will read.
    /// Defaults to the seed basket in <c>hqqq-reference-data</c>.
    /// </summary>
    public string BasketId { get; set; } = "HQQQ";

    /// <summary>
    /// Inclusive lower bound of the report window. Required when
    /// <see cref="Mode"/> is <see cref="ReportMode"/>.
    /// </summary>
    public DateTimeOffset? StartUtc { get; set; }

    /// <summary>
    /// Inclusive upper bound of the report window. Required when
    /// <see cref="Mode"/> is <see cref="ReportMode"/> and must be
    /// strictly after <see cref="StartUtc"/>.
    /// </summary>
    public DateTimeOffset? EndUtc { get; set; }

    /// <summary>
    /// Optional filesystem path for a JSON artifact copy of the summary.
    /// When set, parent directories are created on demand. When null,
    /// the job logs the summary but does not persist any file.
    /// </summary>
    public string? EmitJsonPath { get; set; }

    /// <summary>
    /// Hard cap on the number of snapshot rows loaded for a single report
    /// run. The reader selects <c>LIMIT MaxRows + 1</c> so the job can
    /// fail fast with a clear error rather than silently truncating.
    /// </summary>
    public int MaxRows { get; set; } = 1_000_000;

    /// <summary>
    /// When true, the job additionally loads cheap raw-tick aggregates for
    /// the same window. Off by default so a report run never depends on
    /// the <c>raw_ticks</c> table being populated.
    /// </summary>
    public bool IncludeRawTickAggregates { get; set; }

    /// <summary>
    /// Set of <c>quote_quality</c> string values that count as "stale" for
    /// the stale-ratio metric. Matching is case-insensitive. Defaults to
    /// <c>stale</c> and <c>degraded</c>, mirroring the quote-engine's
    /// vocabulary.
    /// </summary>
    public string[] StaleQualityStates { get; set; } = new[] { "stale", "degraded" };

    /// <summary>
    /// Top-N cap on the number of detected time gaps reported in the
    /// summary. Kept small so the summary stays log-friendly.
    /// </summary>
    public int TopGapCount { get; set; } = 5;
}
