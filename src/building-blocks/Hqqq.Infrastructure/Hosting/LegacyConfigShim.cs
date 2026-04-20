using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hqqq.Infrastructure.Hosting;

/// <summary>
/// Bridges legacy flat environment variable names to hierarchical
/// config keys. Emits a deprecation warning when a flat key is used.
/// </summary>
public static class LegacyConfigShim
{
    private static readonly (string FlatKey, string HierarchicalKey)[] Mappings =
    [
        ("TIINGO_API_KEY",          "Tiingo:ApiKey"),
        ("TIINGO_WS_URL",          "Tiingo:WsUrl"),
        ("TIINGO_REST_BASE_URL",   "Tiingo:RestBaseUrl"),
        ("KAFKA_BOOTSTRAP_SERVERS","Kafka:BootstrapServers"),
        ("REDIS_CONFIGURATION",    "Redis:Configuration"),
        // Phase 2 standalone-mode toggle. The hierarchical form
        // OperatingMode (scalar) is accepted directly by
        // OperatingModeRegistration; the flat HQQQ_OPERATING_MODE alias
        // mirrors the existing Phase 1 HQQQ_* convention so operators
        // can stay in one naming scheme across both phases.
        ("HQQQ_OPERATING_MODE",    "OperatingMode"),
    ];

    /// <summary>
    /// Adds an in-memory overlay that fills missing hierarchical keys
    /// from their legacy flat counterparts. Should be called early in
    /// host builder configuration.
    /// </summary>
    public static IConfigurationBuilder AddLegacyFlatKeyFallback(
        this IConfigurationBuilder builder, ILogger? logger = null)
    {
        var fallbacks = new Dictionary<string, string?>();

        foreach (var (flat, hierarchical) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(flat);
            if (string.IsNullOrEmpty(value))
                continue;

            var envStyleKey = hierarchical.Replace(":", "__");
            var existing = Environment.GetEnvironmentVariable(envStyleKey);
            if (!string.IsNullOrEmpty(existing))
                continue;

            fallbacks[hierarchical] = value;
            logger?.LogWarning(
                "Legacy env var {FlatKey} detected — mapped to {HierarchicalKey}. " +
                "Please migrate to the hierarchical key (e.g., {EnvKey})",
                flat, hierarchical, envStyleKey);
        }

        if (fallbacks.Count > 0)
            builder.AddInMemoryCollection(fallbacks);

        return builder;
    }
}
