using Microsoft.Extensions.Hosting;

namespace Hqqq.Gateway.Configuration;

public enum GatewayDataSourceMode
{
    Stub,
    Legacy,
    Redis,
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
/// <see cref="GatewayOptions.DataSource"/>. Only <c>Quote</c> and
/// <c>Constituents</c> are currently exposed — history and system-health
/// remain on the global mode until later phases (C3 / observability).
/// </summary>
public sealed class GatewaySourcesOptions
{
    public string? Quote { get; set; }
    public string? Constituents { get; set; }
}

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public const string DefaultBasketId = "HQQQ";

    public string? DataSource { get; set; }
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

    private GatewayDataSourceMode ResolveEndpointMode(string? perEndpoint, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(perEndpoint))
        {
            var trimmed = perEndpoint.Trim();
            if (trimmed.Equals("redis", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Redis;
            if (trimmed.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Legacy;
            if (trimmed.Equals("stub", StringComparison.OrdinalIgnoreCase))
                return GatewayDataSourceMode.Stub;
        }

        return ResolveMode(env);
    }
}
