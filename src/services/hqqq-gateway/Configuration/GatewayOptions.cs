using Microsoft.Extensions.Hosting;

namespace Hqqq.Gateway.Configuration;

public enum GatewayDataSourceMode
{
    Stub,
    Legacy,
    Redis,
    Timescale,
    Aggregated,
}

/// <summary>
/// Snapshot of the globally-resolved mode. Preserved for back-compat with
/// existing consumers that only care about the legacy global switch.
/// </summary>
public sealed record ResolvedGatewayMode(GatewayDataSourceMode Mode);

/// <summary>
/// Snapshot of the per-endpoint resolved modes after applying Sources
/// overrides on top of the global <see cref="GatewayOptions.DataSource"/>.
/// Registered as a singleton so tests and observability can inspect exactly
/// which adapter is bound to each interface.
/// </summary>
public sealed record ResolvedSourceModes(
    GatewayDataSourceMode Quote,
    GatewayDataSourceMode Constituents,
    GatewayDataSourceMode History,
    GatewayDataSourceMode SystemHealth);

/// <summary>
/// Per-endpoint source-mode overrides. Each property is optional; when
/// <c>null</c>/empty the endpoint falls back to the global
/// <see cref="GatewayOptions.DataSource"/>. <c>Quote</c>, <c>Constituents</c>,
/// and <c>History</c> support per-endpoint overrides;
/// system-health still follows only the global switch until a later
/// observability step.
/// </summary>
public sealed class GatewaySourcesOptions
{
    public string? Quote { get; set; }
    public string? Constituents { get; set; }

    /// <summary>
    /// History source override. Supported values:
    /// <c>stub</c>, <c>legacy</c>, <c>timescale</c>, or empty to inherit the
    /// global <see cref="GatewayOptions.DataSource"/> (stub/legacy only).
    /// </summary>
    public string? History { get; set; }

    /// <summary>
    /// System-health source override. Supported values:
    /// <c>aggregated</c> (default), <c>stub</c>, <c>legacy</c>. Empty
    /// resolves to <c>aggregated</c> regardless of the global
    /// <see cref="GatewayOptions.DataSource"/> so the native aggregator is
    /// the new out-of-the-box behavior.
    /// </summary>
    public string? SystemHealth { get; set; }
}

/// <summary>
/// Strongly-typed gateway configuration.
/// </summary>
/// <remarks>
/// <para>
/// The default Phase 2 posture is the per-endpoint mix wired in
/// <c>docker-compose.phase2.yml</c>: <c>Gateway:Sources:Quote=redis</c>,
/// <c>…Constituents=redis</c>, <c>…History=timescale</c>,
/// <c>…SystemHealth=aggregated</c>. None of those require the legacy
/// <c>hqqq-api</c> monolith.
/// </para>
/// <para>
/// <see cref="LegacyBaseUrl"/> and the <c>"legacy"</c> per-endpoint
/// values are kept solely for side-by-side parity testing against a
/// separately-running monolith. They are <b>not</b> the supported
/// Phase 2 path; <c>Program.cs</c> emits a loud
/// <c>[gateway:legacy-mode]</c> warning at startup whenever any
/// endpoint resolves to <see cref="GatewayDataSourceMode.Legacy"/>.
/// </para>
/// </remarks>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public const string DefaultBasketId = "HQQQ";

    public string? DataSource { get; set; }

    /// <summary>
    /// Optional base URL for the legacy <c>hqqq-api</c> monolith.
    /// Required when any endpoint resolves to
    /// <see cref="GatewayDataSourceMode.Legacy"/>; ignored otherwise.
    /// Leaving this empty keeps the legacy HttpClient out of DI entirely
    /// and is the recommended Phase 2 default.
    /// </summary>
    public string? LegacyBaseUrl { get; set; }

    /// <summary>
    /// Basket id used to format Redis keys for quote/constituents sources
    /// (<c>hqqq:snapshot:{basketId}</c> / <c>hqqq:constituents:{basketId}</c>).
    /// Defaults to <see cref="DefaultBasketId"/> which matches the seed basket
    /// in <c>hqqq-reference-data</c>'s in-memory repository.
    /// </summary>
    public string? BasketId { get; set; }

    public GatewaySourcesOptions Sources { get; set; } = new();

    /// <summary>Effective basket id with default applied.</summary>
    public string ResolveBasketId()
        => string.IsNullOrWhiteSpace(BasketId) ? DefaultBasketId : BasketId.Trim();

    /// <summary>
    /// Legacy global resolver: returns <see cref="GatewayDataSourceMode.Stub"/>
    /// or <see cref="GatewayDataSourceMode.Legacy"/> only. Used as the fallback
    /// for endpoints that have no per-endpoint override and for history /
    /// system-health which stay on the B1 transitional path.
    /// </summary>
    public GatewayDataSourceMode ResolveMode(IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(DataSource))
        {
            return DataSource.Trim().Equals("legacy", StringComparison.OrdinalIgnoreCase)
                ? GatewayDataSourceMode.Legacy
                : GatewayDataSourceMode.Stub;
        }

        if (!string.IsNullOrWhiteSpace(LegacyBaseUrl) && env.IsDevelopment())
            return GatewayDataSourceMode.Legacy;

        return GatewayDataSourceMode.Stub;
    }

    public GatewayDataSourceMode ResolveQuoteMode(IHostEnvironment env)
        => ResolveEndpointMode(Sources.Quote, env);

    public GatewayDataSourceMode ResolveConstituentsMode(IHostEnvironment env)
        => ResolveEndpointMode(Sources.Constituents, env);

    /// <summary>
    /// Resolves the per-endpoint mode for <c>/api/history</c>. Accepts
    /// <c>stub</c>, <c>legacy</c>, and <c>timescale</c> overrides; empty
    /// falls back to the global <see cref="DataSource"/> (stub/legacy only).
    /// <c>redis</c> is not a valid history mode.
    /// </summary>
    public GatewayDataSourceMode ResolveHistoryMode(IHostEnvironment env)
        => ResolveEndpointMode(Sources.History, env);

    /// <summary>
    /// Resolves the per-endpoint mode for <c>/api/system/health</c>. Empty
    /// defaults to <see cref="GatewayDataSourceMode.Aggregated"/> — the new
    /// native aggregator — instead of falling back to the global switch,
    /// so frontends pick up the native behavior automatically.
    /// </summary>
    public GatewayDataSourceMode ResolveSystemHealthMode()
    {
        if (!string.IsNullOrWhiteSpace(Sources.SystemHealth))
        {
            var trimmed = Sources.SystemHealth.Trim();
            if (trimmed.Equals("aggregated", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Aggregated;
            if (trimmed.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Legacy;
            if (trimmed.Equals("stub", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Stub;
        }

        return GatewayDataSourceMode.Aggregated;
    }

    private GatewayDataSourceMode ResolveEndpointMode(string? perEndpoint, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(perEndpoint))
        {
            var trimmed = perEndpoint.Trim();
            if (trimmed.Equals("redis", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Redis;
            if (trimmed.Equals("timescale", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Timescale;
            if (trimmed.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Legacy;
            if (trimmed.Equals("stub", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Stub;
        }

        return ResolveMode(env);
    }
}
