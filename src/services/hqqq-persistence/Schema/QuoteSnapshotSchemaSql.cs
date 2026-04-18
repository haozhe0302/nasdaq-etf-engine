namespace Hqqq.Persistence.Schema;

/// <summary>
/// Idempotent DDL for the <c>quote_snapshots</c> hypertable owned by the
/// persistence service. Statements are exposed individually so each can be
/// executed in its own round-trip (TimescaleDB's <c>create_hypertable</c>
/// returns a rowset) and so tests can assert each guarantee independently
/// without loading a real database.
/// </summary>
/// <remarks>
/// C1 scope: schema only for quote snapshots. Raw ticks, constituent
/// snapshots, rollups, and retention policies are deferred to C2/C4.
/// </remarks>
public static class QuoteSnapshotSchemaSql
{
    /// <summary>
    /// Creates the base table with the exact column set written by
    /// <see cref="Persistence.QuoteSnapshotSqlCommands.InsertSql"/> plus
    /// the <c>inserted_at_utc</c> DEFAULT column preserved on replay. The
    /// <c>UNIQUE (basket_id, ts)</c> constraint is the conflict target that
    /// makes writes idempotent.
    /// </summary>
    public const string CreateTable = """
        CREATE TABLE IF NOT EXISTS quote_snapshots (
            basket_id              text             NOT NULL,
            ts                     timestamptz      NOT NULL,
            nav                    numeric          NOT NULL,
            market_proxy_price     numeric          NOT NULL,
            premium_discount_pct   numeric          NOT NULL,
            stale_count            integer          NOT NULL,
            fresh_count            integer          NOT NULL,
            max_component_age_ms   double precision NOT NULL,
            quote_quality          text             NOT NULL,
            inserted_at_utc        timestamptz      NOT NULL DEFAULT now(),
            CONSTRAINT uq_quote_snapshots_basket_ts UNIQUE (basket_id, ts)
        );
        """;

    /// <summary>
    /// Promotes the table to a TimescaleDB hypertable partitioned on
    /// <c>ts</c>. <c>if_not_exists => TRUE</c> keeps the call safe across
    /// repeated startups.
    /// </summary>
    public const string CreateHypertable =
        "SELECT create_hypertable('quote_snapshots', 'ts', if_not_exists => TRUE);";

    /// <summary>
    /// Read-side helper index for basket-scoped history queries going
    /// newest-first. Matches the shape of the C3 gateway
    /// <c>/api/history</c> access pattern.
    /// </summary>
    public const string CreateReadIndex = """
        CREATE INDEX IF NOT EXISTS ix_quote_snapshots_basket_ts_desc
            ON quote_snapshots (basket_id, ts DESC);
        """;

    /// <summary>
    /// Ordered list of idempotent statements executed by
    /// <see cref="QuoteSnapshotSchemaBootstrapper"/>. Order matters:
    /// table first, then hypertable, then read index.
    /// </summary>
    public static readonly IReadOnlyList<string> Statements = new[]
    {
        CreateTable,
        CreateHypertable,
        CreateReadIndex,
    };
}
