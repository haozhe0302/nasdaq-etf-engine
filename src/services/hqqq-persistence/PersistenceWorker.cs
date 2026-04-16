using Microsoft.Extensions.Options;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Timescale;

namespace Hqqq.Persistence;

public sealed class PersistenceWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<TimescaleOptions> timescaleOptions,
    ILogger<PersistenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PersistenceWorker starting");
        logger.LogInformation("  Kafka: {Kafka}", kafkaOptions.Value.BootstrapServers);
        logger.LogInformation("  Timescale: configured={Configured}",
            !string.IsNullOrWhiteSpace(timescaleOptions.Value.ConnectionString));
        logger.LogInformation("  Status: idle — waiting for Phase 2B consumer/writer wiring");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: Phase 2B — consume from Kafka topics and batch-write to Timescale
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("PersistenceWorker stopping");
    }
}
