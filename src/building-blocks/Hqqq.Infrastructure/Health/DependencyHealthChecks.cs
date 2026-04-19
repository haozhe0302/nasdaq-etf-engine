using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Hqqq.Infrastructure.Health;

/// <summary>
/// Reusable health check implementations for shared infrastructure dependencies.
/// Each returns <see cref="HealthCheckResult.Degraded"/> with a descriptive reason
/// rather than <see cref="HealthCheckResult.Unhealthy"/> when a dependency is
/// unavailable — this supports the Phase 2 principle of "degraded, not crashed".
/// </summary>
public sealed class KafkaHealthCheck(
    Hqqq.Infrastructure.Kafka.KafkaOptions options,
    ILogger<KafkaHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var admin = new Confluent.Kafka.AdminClientBuilder(
                Hqqq.Infrastructure.Kafka.KafkaConfigBuilder.BuildAdminConfig(
                    options, socketTimeoutMs: 3000)).Build();
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(3));
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Connected to {metadata.Brokers.Count} broker(s)"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kafka health check failed");
            return Task.FromResult(HealthCheckResult.Degraded(
                "Kafka unavailable", ex));
        }
    }
}

public sealed class RedisHealthCheck(
    Hqqq.Infrastructure.Redis.RedisOptions options,
    ILogger<RedisHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var mux = await StackExchange.Redis.ConnectionMultiplexer
                .ConnectAsync(new StackExchange.Redis.ConfigurationOptions
                {
                    EndPoints = { options.Configuration },
                    ConnectTimeout = 3000,
                    AbortOnConnectFail = false,
                });
            var db = mux.GetDatabase();
            var pong = await db.PingAsync();
            return HealthCheckResult.Healthy($"Redis ping: {pong.TotalMilliseconds:F1}ms");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis health check failed");
            return HealthCheckResult.Degraded("Redis unavailable", ex);
        }
    }
}

public sealed class TimescaleHealthCheck(
    Hqqq.Infrastructure.Timescale.TimescaleOptions options,
    ILogger<TimescaleHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(options.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("TimescaleDB reachable");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Timescale health check failed");
            return HealthCheckResult.Degraded("TimescaleDB unavailable", ex);
        }
    }
}
