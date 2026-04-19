using Microsoft.Extensions.Logging;

namespace Hqqq.Observability.Logging;

/// <summary>
/// Logging defaults shared across Phase 2 services.
/// Observability registration (metrics, identity, health) lives in
/// <see cref="Hosting.ObservabilityRegistration"/>.
/// </summary>
public static class LoggingExtensions
{
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
