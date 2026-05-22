using Hqqq.Persistence.Options;
using Hqqq.Persistence.Schema;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Plain <see cref="IHostedService"/> (not a <see cref="BackgroundService"/>)
/// so <see cref="StartAsync"/> runs to completion before the consumers and
/// workers start. Base hypertable bootstrap failures propagate → host fails
/// fast: we should not begin consuming <c>pricing.snapshots.v1</c> or
/// <c>market.raw_ticks.v1</c> before the destination tables exist.
/// </summary>
/// <remarks>
/// <para>
/// Order is important: base hypertables first, then continuous-aggregate
/// rollups built on top of <c>quote_snapshots</c>, then retention policies
/// which attach to all of the above. Each step is idempotent on its own
/// so a partial prior run is safe to re-run from scratch. Toggle the
/// overall bootstrap via
/// <see cref="PersistenceOptions.SchemaBootstrapOnStart"/> in environments
/// where schema is owned by an external migration process.
/// </para>
/// <para>
/// Continuous aggregates and retention policies are Timescale community
/// (TSL) features and are rejected with SQLSTATE <c>0A000</c> on the
/// Apache-only build shipped with managed services like Azure Database
/// for PostgreSQL Flexible Server. They are made optional and non-fatal
/// here via <see cref="PersistenceOptions.EnableContinuousAggregates"/>,
/// <see cref="PersistenceOptions.EnableRetentionPolicies"/>, and
/// <see cref="PersistenceOptions.ContinueOnUnsupportedRollups"/> so the
/// persistence service still starts and <c>/api/history</c> still serves
/// from <c>quote_snapshots</c> directly.
/// </para>
/// </remarks>
public sealed class SchemaBootstrapHostedService : IHostedService
{
    private readonly QuoteSnapshotSchemaBootstrapper _snapshotSchema;
    private readonly RawTickSchemaBootstrapper _rawTickSchema;
    private readonly QuoteSnapshotRollupBootstrapper _rollups;
    private readonly RetentionPolicyBootstrapper _retention;
    private readonly PersistenceOptions _options;
    private readonly ILogger<SchemaBootstrapHostedService> _logger;

    public SchemaBootstrapHostedService(
        QuoteSnapshotSchemaBootstrapper snapshotSchema,
        RawTickSchemaBootstrapper rawTickSchema,
        QuoteSnapshotRollupBootstrapper rollups,
        RetentionPolicyBootstrapper retention,
        IOptions<PersistenceOptions> options,
        ILogger<SchemaBootstrapHostedService> logger)
    {
        _snapshotSchema = snapshotSchema;
        _rawTickSchema = rawTickSchema;
        _rollups = rollups;
        _retention = retention;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        SchemaBootstrapPipeline.RunAsync(
            _options,
            _snapshotSchema.EnsureAsync,
            _rawTickSchema.EnsureAsync,
            _rollups.EnsureAsync,
            (includeRollups, ct) => _retention.EnsureAsync(ct, includeRollups),
            _logger,
            cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
