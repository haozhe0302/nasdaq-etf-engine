using Hqqq.Observability.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hqqq.Observability.Logging;

/// <summary>
/// Extension methods for consistent logging and observability registration
/// across all Phase 2 services.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Registers shared observability services (metrics, logging defaults).
    /// </summary>
    public static IServiceCollection AddHqqqObservability(this IServiceCollection services)
    {
        services.AddSingleton<HqqqMetrics>();
        return services;
    }

    /// <summary>
    /// Configures structured console logging with consistent defaults.
    /// </summary>
    public static ILoggingBuilder AddHqqqDefaults(this ILoggingBuilder builder)
    {
        builder.AddSimpleConsole(o =>
        {
            o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            o.SingleLine = true;
            o.IncludeScopes = true;
        });
        return builder;
    }
}
