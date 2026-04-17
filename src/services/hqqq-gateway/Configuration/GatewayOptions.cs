using Microsoft.Extensions.Hosting;

namespace Hqqq.Gateway.Configuration;

public enum GatewayDataSourceMode
{
    Stub,
    Legacy,
}

/// <summary>
/// Wrapper so the resolved mode can be registered as a singleton in DI.
/// </summary>
public sealed record ResolvedGatewayMode(GatewayDataSourceMode Mode);

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string? DataSource { get; set; }
    public string? LegacyBaseUrl { get; set; }

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
}
