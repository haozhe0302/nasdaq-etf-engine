using Hqqq.Persistence.Options;
using Hqqq.Persistence.Schema;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Plain <see cref="IHostedService"/> (not a <see cref="BackgroundService"/>)
/// so <see cref="StartAsync"/> runs to completion before the consumers and
/// workers start. Schema bootstrap failures propagate → host fails fast,
/// which is the desired behavior: we should not begin consuming
/// <c>pricing.snapshots.v1</c> or <c>market.raw_ticks.v1</c> before the
/// destination tables, rollups, and retention policies exist.
/// </summary>
/// <remarks>
/// Order is important: base hypertables first, then continuous-aggregate
/// rollups built on top of <c>quote_snapshots</c>, then retention policies
/// which attach to all of the above. Each step is idempotent on its own
/// so a partial prior run is safe to re-run from scratch. Toggle the whole
/// bootstrap via <see cref="PersistenceOptions.SchemaBootstrapOnStart"/>
/// in environments where schema is owned by an external migration process.
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.SchemaBootstrapOnStart)
        {
            _logger.LogInformation(
                "Persistence:SchemaBootstrapOnStart=false — skipping schema bootstrap");
            return;
        }

        await _snapshotSchema.EnsureAsync(cancellationToken).ConfigureAwait(false);
        await _rawTickSchema.EnsureAsync(cancellationToken).ConfigureAwait(false);
        await _rollups.EnsureAsync(cancellationToken).ConfigureAwait(false);
        await _retention.EnsureAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
