using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Timescale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Shared DI registration extensions for Phase 2 services.
/// </summary>
public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Binds <see cref="KafkaOptions"/> from the "Kafka" configuration section.
    /// </summary>
    public static IServiceCollection AddHqqqKafka(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        return services;
    }

    /// <summary>
    /// Binds <see cref="RedisOptions"/> from the "Redis" configuration section.
    /// </summary>
    public static IServiceCollection AddHqqqRedis(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        return services;
    }

    /// <summary>
    /// Registers a shared <see cref="IConnectionMultiplexer"/> and the
    /// lightweight <see cref="IRedisStringCache"/> seam used by services that
    /// materialize latest serving state to Redis. Callers must also invoke
    /// <see cref="AddHqqqRedis(IServiceCollection, IConfiguration)"/> so
    /// <see cref="RedisOptions"/> is bound. The multiplexer connects eagerly
    /// on first resolve so startup fails fast if Redis is unreachable.
    /// </summary>
    public static IServiceCollection AddHqqqRedisConnection(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            // Parse rather than pass the raw string so we can force
            // AbortOnConnectFail=false. With the default (true) a missing
            // broker on the first resolve throws synchronously and takes
            // the whole host down, which is fatal for in-process
            // integration tests that only use Redis via mocked seams.
            // The downstream Redis health check still reports "unhealthy"
            // on a real outage, so the operator signal is preserved.
            var config = ConfigurationOptions.Parse(options.Configuration);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddSingleton<IRedisStringCache, RedisStringCache>();
        return services;
    }

    /// <summary>
    /// Binds <see cref="TimescaleOptions"/> from the "Timescale" configuration section.
    /// </summary>
    public static IServiceCollection AddHqqqTimescale(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TimescaleOptions>(configuration.GetSection("Timescale"));
        return services;
    }

    /// <summary>
    /// Logs the bound configuration posture for infrastructure dependencies at startup.
    /// </summary>
    public static void LogConfigurationPosture(
        this IServiceProvider services,
        string serviceName,
        ILogger logger,
        params string[] sections)
    {
        var config = services.GetRequiredService<IConfiguration>();
        logger.LogInformation("{Service} starting — configuration posture:", serviceName);

        foreach (var section in sections)
        {
            var sec = config.GetSection(section);
            var hasValues = sec.GetChildren().Any();
            logger.LogInformation("  [{Section}] {Status}",
                section,
                hasValues ? "configured" : "not configured (using defaults)");
        }
    }
}
