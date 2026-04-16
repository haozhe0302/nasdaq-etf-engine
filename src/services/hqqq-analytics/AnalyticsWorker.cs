using Microsoft.Extensions.Options;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Timescale;

namespace Hqqq.Analytics;

public sealed class AnalyticsWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<TimescaleOptions> timescaleOptions,
    ILogger<AnalyticsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnalyticsWorker starting");
        logger.LogInformation("  Kafka: {Kafka}", kafkaOptions.Value.BootstrapServers);
        logger.LogInformation("  Timescale: configured={Configured}",
            !string.IsNullOrWhiteSpace(timescaleOptions.Value.ConnectionString));
        logger.LogInformation("  Status: idle — waiting for Phase 2B/C job wiring");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: Phase 2B — consume ops.incidents.v1
            // TODO: Phase 2C — add replay/backfill and anomaly detection jobs
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("AnalyticsWorker stopping");
    }
}
