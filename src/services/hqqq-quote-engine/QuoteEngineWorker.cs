using Microsoft.Extensions.Options;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;

namespace Hqqq.QuoteEngine;

public sealed class QuoteEngineWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<RedisOptions> redisOptions,
    ILogger<QuoteEngineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QuoteEngineWorker starting");
        logger.LogInformation("  Kafka: {Kafka}", kafkaOptions.Value.BootstrapServers);
        logger.LogInformation("  Redis: {Redis}", redisOptions.Value.Configuration);
        logger.LogInformation("  Status: idle — waiting for Phase 2B consumer/producer wiring");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: Phase 2B — consume ticks, compute iNAV, write to Redis, publish snapshots
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("QuoteEngineWorker stopping");
    }
}
