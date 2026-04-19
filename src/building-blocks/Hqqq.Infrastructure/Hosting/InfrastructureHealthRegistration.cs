using Hqqq.Infrastructure.Health;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Timescale;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Adds the shared infrastructure dependency probes (Kafka, Redis,
/// Timescale) to an <see cref="IHealthChecksBuilder"/>. Each probe is
/// registered with the <c>"ready"</c> tag so it surfaces on
/// <c>/healthz/ready</c> but never blocks <c>/healthz/live</c>.
/// All probes return <see cref="HealthCheckResult.Degraded"/> on failure
/// (degraded, not crashed) so a missing dependency reports posture
/// without taking the whole process down.
/// </summary>
public static class InfrastructureHealthRegistration
{
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddKafkaHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "kafka")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new KafkaHealthCheck(
                sp.GetRequiredService<IOptions<KafkaOptions>>().Value,
                sp.GetService<ILogger<KafkaHealthCheck>>() ?? NullLogger<KafkaHealthCheck>.Instance),
            failureStatus: null,
            tags: new[] { ReadyTag }));
    }

    public static IHealthChecksBuilder AddRedisHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "redis")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new RedisHealthCheck(
                sp.GetRequiredService<IOptions<RedisOptions>>().Value,
                sp.GetService<ILogger<RedisHealthCheck>>() ?? NullLogger<RedisHealthCheck>.Instance),
            failureStatus: null,
            tags: new[] { ReadyTag }));
    }

    public static IHealthChecksBuilder AddTimescaleHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "timescale")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new TimescaleHealthCheck(
                sp.GetRequiredService<IOptions<TimescaleOptions>>().Value,
                sp.GetService<ILogger<TimescaleHealthCheck>>() ?? NullLogger<TimescaleHealthCheck>.Instance),
            failureStatus: null,
            tags: new[] { ReadyTag }));
    }
}
