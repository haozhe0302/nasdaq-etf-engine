using Hqqq.Persistence.Options;
using Npgsql;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Pure orchestration of the persistence schema bootstrap so the if /
/// try-catch matrix governing
/// <see cref="PersistenceOptions.EnableContinuousAggregates"/>,
/// <see cref="PersistenceOptions.EnableRetentionPolicies"/>, and
/// <see cref="PersistenceOptions.ContinueOnUnsupportedRollups"/> can be
/// exercised without a live TimescaleDB instance. The hosted service
/// (<see cref="SchemaBootstrapHostedService"/>) is just a thin DI shell
/// over this method.
/// </summary>
/// <remarks>
/// <para>
/// Order — and which steps are gated by which options:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       Base hypertables (<c>quote_snapshots</c>, <c>raw_ticks</c>):
///       always required. Failures here are fatal (no <c>0A000</c>
///       handling) — a managed PostgreSQL environment that cannot create
///       hypertables cannot host this service at all.
///     </description>
///   </item>
///   <item>
///     <description>
///       Continuous-aggregate rollups: gated by
///       <see cref="PersistenceOptions.EnableContinuousAggregates"/>. On
///       <c>0A000</c> (Apache-licensed TimescaleDB rejects continuous
///       aggregates) the failure is downgraded to a warning when
///       <see cref="PersistenceOptions.ContinueOnUnsupportedRollups"/> is
///       true, and rollup-scoped retention policies are skipped.
///     </description>
///   </item>
///   <item>
///     <description>
///       Retention policies: gated by
///       <see cref="PersistenceOptions.EnableRetentionPolicies"/>. Rollup
///       views are only included if the rollup step succeeded. <c>0A000</c>
///       here (e.g. <c>add_retention_policy</c> rejected by Apache-only
///       Timescale) is also downgraded to a warning under
///       <see cref="PersistenceOptions.ContinueOnUnsupportedRollups"/>.
///     </description>
///   </item>
/// </list>
/// </remarks>
internal static class SchemaBootstrapPipeline
{
    /// <summary>SQLSTATE returned by PostgreSQL / TimescaleDB when a
    /// feature is unavailable — e.g. continuous aggregates under the
    /// Apache-only license. Mirrors
    /// <c>Npgsql.PostgresErrorCodes.FeatureNotSupported</c>.</summary>
    internal const string FeatureNotSupportedSqlState = "0A000";

    /// <summary>
    /// Runs the bootstrap sequence. Each <c>ensure*</c> delegate
    /// represents one step; tests substitute fakes that record calls or
    /// throw <see cref="PostgresException"/> with SQLSTATE
    /// <c>0A000</c>.
    /// </summary>
    /// <param name="options">Persistence options governing which steps run.</param>
    /// <param name="ensureSnapshotSchema">
    /// Idempotent <c>quote_snapshots</c> hypertable bootstrap.
    /// </param>
    /// <param name="ensureRawTickSchema">
    /// Idempotent <c>raw_ticks</c> hypertable bootstrap.
    /// </param>
    /// <param name="ensureRollups">
    /// Idempotent continuous-aggregate rollup bootstrap.
    /// </param>
    /// <param name="ensureRetention">
    /// Retention-policy bootstrap. The bool argument selects whether
    /// rollup views are included as retention targets — must be
    /// <c>false</c> whenever the rollup step did not produce the views.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="cancellationToken">Host cancellation token.</param>
    public static async Task RunAsync(
        PersistenceOptions options,
        Func<CancellationToken, Task> ensureSnapshotSchema,
        Func<CancellationToken, Task> ensureRawTickSchema,
        Func<CancellationToken, Task> ensureRollups,
        Func<bool, CancellationToken, Task> ensureRetention,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ensureSnapshotSchema);
        ArgumentNullException.ThrowIfNull(ensureRawTickSchema);
        ArgumentNullException.ThrowIfNull(ensureRollups);
        ArgumentNullException.ThrowIfNull(ensureRetention);
        ArgumentNullException.ThrowIfNull(logger);

        if (!options.SchemaBootstrapOnStart)
        {
            logger.LogInformation(
                "Persistence:SchemaBootstrapOnStart=false — skipping schema bootstrap entirely");
            return;
        }

        // Base hypertables are non-negotiable: /api/history reads from
        // quote_snapshots directly, and the ingest pipeline lands in
        // raw_ticks. Any failure here is fatal by design.
        await ensureSnapshotSchema(cancellationToken).ConfigureAwait(false);
        await ensureRawTickSchema(cancellationToken).ConfigureAwait(false);

        var rollupsCreated = false;
        if (options.EnableContinuousAggregates)
        {
            try
            {
                await ensureRollups(cancellationToken).ConfigureAwait(false);
                rollupsCreated = true;
            }
            catch (PostgresException ex) when (
                options.ContinueOnUnsupportedRollups &&
                ex.SqlState == FeatureNotSupportedSqlState)
            {
                logger.LogWarning(
                    ex,
                    "Continuous-aggregate rollups are not supported by the current TimescaleDB license (SQLSTATE {SqlState}); skipping quote_snapshots_1m/quote_snapshots_5m and their retention policies. /api/history will continue to serve from quote_snapshots.",
                    ex.SqlState);
            }
        }
        else
        {
            logger.LogInformation(
                "Persistence:EnableContinuousAggregates=false — skipping continuous-aggregate rollups and rollup-specific retention policies");
        }

        if (options.EnableRetentionPolicies)
        {
            try
            {
                await ensureRetention(rollupsCreated, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (
                options.ContinueOnUnsupportedRollups &&
                ex.SqlState == FeatureNotSupportedSqlState)
            {
                logger.LogWarning(
                    ex,
                    "Timescale retention policies are not supported by the current TimescaleDB license (SQLSTATE {SqlState}); skipping add_retention_policy registration. Data will not be aged out automatically — manage retention externally.",
                    ex.SqlState);
            }
        }
        else
        {
            logger.LogInformation(
                "Persistence:EnableRetentionPolicies=false — skipping add_retention_policy registration");
        }
    }
}
