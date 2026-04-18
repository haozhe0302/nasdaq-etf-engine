using Hqqq.Persistence.Options;
using Hqqq.Persistence.Schema;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Plain <see cref="IHostedService"/> (not a <see cref="BackgroundService"/>)
/// so <see cref="StartAsync"/> runs to completion before the consumer and
/// worker start. Schema bootstrap failures propagate → host fails fast,
/// which is the desired behavior: we should not begin consuming
/// <c>pricing.snapshots.v1</c> before the destination table exists.
/// </summary>
/// <remarks>
/// Toggle off via <see cref="PersistenceOptions.SchemaBootstrapOnStart"/>
/// in environments where schema is owned by an external migration process.
/// </remarks>
public sealed class SchemaBootstrapHostedService : IHostedService
{
    private readonly QuoteSnapshotSchemaBootstrapper _bootstrapper;
    private readonly PersistenceOptions _options;
    private readonly ILogger<SchemaBootstrapHostedService> _logger;

    public SchemaBootstrapHostedService(
        QuoteSnapshotSchemaBootstrapper bootstrapper,
        IOptions<PersistenceOptions> options,
        ILogger<SchemaBootstrapHostedService> logger)
    {
        _bootstrapper = bootstrapper;
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

        await _bootstrapper.EnsureAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
