namespace Hqqq.Persistence.Options;

/// <summary>
/// Persistence-only knobs bound from the "Persistence" configuration section.
/// Kafka topic names, connection strings, and JSON defaults live in shared
/// infrastructure (<see cref="Hqqq.Infrastructure.Kafka.KafkaTopics"/>,
/// <see cref="Hqqq.Infrastructure.Kafka.KafkaOptions"/>,
/// <see cref="Hqqq.Infrastructure.Timescale.TimescaleOptions"/>,
/// <see cref="Hqqq.Infrastructure.Serialization.HqqqJsonDefaults"/>) and are
/// intentionally not duplicated here.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// When true (default), the service ensures the Timescale schema on
    /// startup via the registered bootstrappers (quote snapshots, raw ticks,
    /// rollups, retention policies). Bootstrap failures fail the host fast;
    /// malformed Kafka events never do. Disable in tests or environments
    /// where a migration/owner process has already provisioned the tables.
    /// </summary>
    public bool SchemaBootstrapOnStart { get; set; } = true;

    // ── Quote snapshot pipeline (C1) ──

    /// <summary>
    /// Maximum number of snapshot rows combined into a single Timescale
    /// INSERT. A flush also triggers when <see cref="SnapshotFlushInterval"/>
    /// elapses.
    /// </summary>
    public int SnapshotWriteBatchSize { get; set; } = 128;

    /// <summary>
    /// Upper bound on the delay between receiving a snapshot and writing it,
    /// even if the batch is only partially full. Keeps the write tail short
    /// at low ingest rates.
    /// </summary>
    public TimeSpan SnapshotFlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Capacity of the in-proc channel between the snapshot consumer and
    /// worker. Bounded on purpose so Kafka consumption applies backpressure
    /// when the writer cannot keep up.
    /// </summary>
    public int SnapshotChannelCapacity { get; set; } = 2048;

    // ── Raw tick pipeline (C3) ──

    /// <summary>
    /// Maximum number of raw-tick rows combined into a single Timescale
    /// INSERT. Raw ticks arrive at higher volume than snapshots so the
    /// default is larger. A flush also triggers when
    /// <see cref="RawTickFlushInterval"/> elapses.
    /// </summary>
    public int RawTickWriteBatchSize { get; set; } = 256;

    /// <summary>
    /// Upper bound on the delay between receiving a raw tick and writing it,
    /// even if the batch is only partially full.
    /// </summary>
    public TimeSpan RawTickFlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Capacity of the in-proc channel between the raw-tick consumer and
    /// worker. Larger than the snapshot channel because raw ticks arrive
    /// per symbol per provider update rather than once per compute cycle.
    /// </summary>
    public int RawTickChannelCapacity { get; set; } = 8192;

    // ── Retention windows (C3 groundwork) ──

    /// <summary>
    /// Retention window applied to the <c>raw_ticks</c> hypertable by
    /// Timescale's <c>add_retention_policy</c>. Raw ticks are the highest
    /// volume table; default is short because the primary long-range use
    /// case is the snapshot rollups below.
    /// </summary>
    public TimeSpan RawTickRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Retention window applied to the <c>quote_snapshots</c> hypertable.
    /// Longer than raw ticks because snapshots are the authoritative
    /// serving source for <c>/api/history</c> today.
    /// </summary>
    public TimeSpan QuoteSnapshotRetention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Retention window applied to the continuous-aggregate rollups
    /// (<c>quote_snapshots_1m</c>, <c>quote_snapshots_5m</c>). Longer than
    /// the base snapshot table so rollups outlive the raw snapshots they
    /// were built from — the whole point of the rollup groundwork.
    /// </summary>
    public TimeSpan RollupRetention { get; set; } = TimeSpan.FromDays(730);
}
