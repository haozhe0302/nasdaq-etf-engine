using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Timescale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
