namespace Hqqq.Persistence.Schema;

/// <summary>
/// Idempotent DDL for the continuous-aggregate rollups built on top of
/// the <c>quote_snapshots</c> hypertable. Groundwork for future
/// longer-range history and analytics queries — the gateway read-side is
/// explicitly not re-pointed at rollups in this step.
/// </summary>
/// <remarks>
/// <para>
/// Two rollups are materialized: <c>quote_snapshots_1m</c> and
/// <c>quote_snapshots_5m</c>. Both use <c>last(col, ts)</c> for the
/// representative NAV / market-proxy / premium-discount value inside the
/// bucket, so the rollup reflects the latest snapshot in that window
/// (the natural choice for a time-series pricing view).
/// </para>
/// <para>
/// Each <c>CREATE MATERIALIZED VIEW</c> is <c>IF NOT EXISTS</c> and
/// includes <c>WITH NO DATA</c> so initial creation is cheap; the
/// continuous-aggregate refresh policy fills in history in the background.
/// Each policy registration is wrapped in a <c>DO ... EXCEPTION WHEN
/// duplicate_object</c> block so repeat startup is safe even though
/// <c>add_continuous_aggregate_policy</c> does not support
/// <c>if_not_exists</c> on older TimescaleDB versions.
/// </para>
/// </remarks>
public static class QuoteSnapshotRollupSchemaSql
{
    /// <summary>1-minute continuous aggregate view definition.</summary>
    public const string CreateOneMinuteView = """
        CREATE MATERIALIZED VIEW IF NOT EXISTS quote_snapshots_1m
        WITH (timescaledb.continuous) AS
        SELECT
            time_bucket('1 minute', ts)           AS bucket,
            basket_id,
            last(nav, ts)                         AS nav,
            last(market_proxy_price, ts)          AS market_proxy_price,
            last(premium_discount_pct, ts)        AS premium_discount_pct,
            count(*)                              AS point_count,
            avg(max_component_age_ms)             AS avg_max_component_age_ms,
            sum(stale_count)                      AS stale_count_sum,
            sum(fresh_count)                      AS fresh_count_sum
        FROM quote_snapshots
        GROUP BY bucket, basket_id
        WITH NO DATA;
        """;

    /// <summary>5-minute continuous aggregate view definition.</summary>
    public const string CreateFiveMinuteView = """
        CREATE MATERIALIZED VIEW IF NOT EXISTS quote_snapshots_5m
        WITH (timescaledb.continuous) AS
        SELECT
            time_bucket('5 minutes', ts)          AS bucket,
            basket_id,
            last(nav, ts)                         AS nav,
            last(market_proxy_price, ts)          AS market_proxy_price,
            last(premium_discount_pct, ts)        AS premium_discount_pct,
            count(*)                              AS point_count,
            avg(max_component_age_ms)             AS avg_max_component_age_ms,
            sum(stale_count)                      AS stale_count_sum,
            sum(fresh_count)                      AS fresh_count_sum
        FROM quote_snapshots
        GROUP BY bucket, basket_id
        WITH NO DATA;
        """;

    /// <summary>
    /// Registers the background refresh policy for
    /// <c>quote_snapshots_1m</c>. Wrapped in a DO block so repeated
    /// startup does not error when the policy already exists.
    /// </summary>
    public const string AddOneMinutePolicy = """
        DO $$
        BEGIN
            PERFORM add_continuous_aggregate_policy(
                'quote_snapshots_1m',
                start_offset => INTERVAL '2 hours',
                end_offset   => INTERVAL '1 minute',
                schedule_interval => INTERVAL '1 minute');
        EXCEPTION WHEN duplicate_object THEN
            NULL;
        END
        $$;
        """;

    /// <summary>
    /// Registers the background refresh policy for
    /// <c>quote_snapshots_5m</c>.
    /// </summary>
    public const string AddFiveMinutePolicy = """
        DO $$
        BEGIN
            PERFORM add_continuous_aggregate_policy(
                'quote_snapshots_5m',
                start_offset => INTERVAL '1 day',
                end_offset   => INTERVAL '5 minutes',
                schedule_interval => INTERVAL '5 minutes');
        EXCEPTION WHEN duplicate_object THEN
            NULL;
        END
        $$;
        """;

    /// <summary>
    /// Ordered list of idempotent statements executed by
    /// <see cref="QuoteSnapshotRollupBootstrapper"/>. Order matters: view
    /// definitions first, then policies.
    /// </summary>
    public static readonly IReadOnlyList<string> Statements = new[]
    {
        CreateOneMinuteView,
        CreateFiveMinuteView,
        AddOneMinutePolicy,
        AddFiveMinutePolicy,
    };

    /// <summary>
    /// Names of the rollup views, in the same order they were created.
    /// Used by <see cref="RetentionPolicySchemaSql"/> so the retention
    /// bootstrapper can apply a rollup-specific window to both views.
    /// </summary>
    public static readonly IReadOnlyList<string> RollupViews = new[]
    {
        "quote_snapshots_1m",
        "quote_snapshots_5m",
    };
}
