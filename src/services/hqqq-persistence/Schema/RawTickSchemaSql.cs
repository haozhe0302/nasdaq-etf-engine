namespace Hqqq.Persistence.Schema;

/// <summary>
/// Idempotent DDL for the <c>raw_ticks</c> hypertable owned by the
/// persistence service. Statements are exposed individually so each can be
/// executed in its own round-trip (TimescaleDB's <c>create_hypertable</c>
/// returns a rowset) and so tests can assert each guarantee independently
/// without loading a real database.
/// </summary>
/// <remarks>
/// <para>
/// Replay-safe identity: <c>UNIQUE (symbol, provider_timestamp, sequence)</c>.
/// </para>
/// <para>
/// Rationale: <c>sequence</c> is monotonic per-provider per-symbol stream,
/// and <c>provider_timestamp</c> anchors it on the natural time axis so a
/// replay of the exact same event lands on the exact same row. <c>provider</c>
/// is intentionally NOT part of the key today because Phase 2 ingress is
/// single-provider; extending the key with <c>provider</c> later is an
/// additive schema change if/when a second provider joins the stream.
/// </para>
/// </remarks>
public static class RawTickSchemaSql
{
    /// <summary>
    /// Creates the base <c>raw_ticks</c> table with the exact column set
    /// written by <see cref="Persistence.RawTickSqlCommands.InsertSql"/>
    /// plus the <c>inserted_at_utc</c> DEFAULT column preserved on replay.
    /// The <c>UNIQUE (symbol, provider_timestamp, sequence)</c> constraint
    /// is the conflict target that makes writes idempotent.
    /// </summary>
    public const string CreateTable = """
        CREATE TABLE IF NOT EXISTS raw_ticks (
            symbol              text             NOT NULL,
            provider_timestamp  timestamptz      NOT NULL,
            ingress_timestamp   timestamptz      NOT NULL,
            last                numeric          NOT NULL,
            bid                 numeric          NULL,
            ask                 numeric          NULL,
            currency            text             NOT NULL,
            provider            text             NOT NULL,
            sequence            bigint           NOT NULL,
            inserted_at_utc     timestamptz      NOT NULL DEFAULT now(),
            CONSTRAINT uq_raw_ticks_symbol_ts_seq UNIQUE (symbol, provider_timestamp, sequence)
        );
        """;

    /// <summary>
    /// Promotes the table to a TimescaleDB hypertable partitioned on
    /// <c>provider_timestamp</c>. <c>if_not_exists => TRUE</c> keeps the
    /// call safe across repeated startups.
    /// </summary>
    public const string CreateHypertable =
        "SELECT create_hypertable('raw_ticks', 'provider_timestamp', if_not_exists => TRUE);";

    /// <summary>
    /// Per-symbol descending-time index for replay/debug queries. Matches
    /// the common "latest ticks for symbol X" access pattern.
    /// </summary>
    public const string CreateSymbolTimeIndex = """
        CREATE INDEX IF NOT EXISTS ix_raw_ticks_symbol_ts_desc
            ON raw_ticks (symbol, provider_timestamp DESC);
        """;

    /// <summary>
    /// Broad descending-time index for cross-symbol replay / debug queries.
    /// Kept intentionally small — we do not over-index in this step.
    /// </summary>
    public const string CreateTimeIndex = """
        CREATE INDEX IF NOT EXISTS ix_raw_ticks_ts_desc
            ON raw_ticks (provider_timestamp DESC);
        """;

    /// <summary>
    /// Ordered list of idempotent statements executed by
    /// <see cref="RawTickSchemaBootstrapper"/>. Order matters: table
    /// first, then hypertable, then indexes.
    /// </summary>
    public static readonly IReadOnlyList<string> Statements = new[]
    {
        CreateTable,
        CreateHypertable,
        CreateSymbolTimeIndex,
        CreateTimeIndex,
    };
}
