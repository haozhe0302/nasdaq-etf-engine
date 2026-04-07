using System.Reflection;
using Hqqq.Api.Modules.System.Contracts;
using Hqqq.Api.Modules.System.Services;
using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.CorporateActions.Contracts;
using Hqqq.Api.Modules.Pricing.Services;

namespace Hqqq.Api.Modules.System;

public static class SystemModule
{
    private static string GetInformationalVersion()
    {
        var asm = typeof(SystemModule).Assembly;
        var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var v = attr?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(v))
            return asm.GetName().Version?.ToString() ?? "0.0.0";

        // Semver build metadata after '+' is often a full git SHA; cap at 8 chars for display/API.
        var plus = v.IndexOf('+');
        if (plus >= 0 && plus < v.Length - 1)
        {
            var suffix = v[(plus + 1)..];
            if (suffix.Length > 8)
                return v[..(plus + 1)] + suffix[..8];
        }

        return v;
    }

    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        services.AddSingleton<MetricsService>();
        return services;
    }

    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/health", (
            IBasketSnapshotProvider basketProvider,
            ILatestPriceStore priceStore,
            IMarketDataIngestionService marketData,
            PricingEngine pricingEngine,
            MetricsService metrics,
            ICorporateActionAdjustmentService adjustmentService) =>
        {
            var bs = basketProvider.GetState();
            var fh = priceStore.GetHealthSnapshot();
            var ss = pricingEngine.CurrentScaleState;

            var status = "healthy";
            if (bs.Active is null)
                status = "unhealthy";
            else if (!ss.IsInitialized)
                status = "degraded";
            else if (fh.StaleSymbolCount > fh.SymbolsTracked / 2)
                status = "degraded";

            var health = new SystemHealth
            {
                ServiceName = "hqqq-api",
                Status = status,
                CheckedAtUtc = DateTimeOffset.UtcNow,
                Version = GetInformationalVersion(),
                Runtime = RuntimeInfo.Capture(),
                Metrics = metrics.GetSnapshot(),
                Upstream = new UpstreamDiagnostics
                {
                    WebSocketConnected = marketData.IsWebSocketConnected,
                    FallbackActive = marketData.IsFallbackActive,
                    LastUpstreamError = marketData.LastUpstreamError,
                    LastUpstreamErrorCode = marketData.LastUpstreamErrorCode,
                    LastUpstreamErrorAtUtc = marketData.LastUpstreamErrorAtUtc,
                },
                Dependencies =
                [
                    new DependencyHealth
                    {
                        Name = "basket",
                        Status = bs.Active is not null ? "healthy" : "unhealthy",
                        LastCheckedAtUtc = DateTimeOffset.UtcNow,
                        Details = bs.Active is not null
                            ? $"{bs.Active.Constituents.Count} constituents"
                            : bs.LastError ?? "No active basket",
                    },
                    new DependencyHealth
                    {
                        Name = "market-data",
                        Status = marketData.IsRunning
                            ? (marketData.IsWebSocketConnected ? "healthy" : "degraded")
                            : "unhealthy",
                        LastCheckedAtUtc = DateTimeOffset.UtcNow,
                        Details = $"ws={marketData.IsWebSocketConnected}, " +
                                  $"fallback={marketData.IsFallbackActive}, " +
                                  $"tracked={fh.SymbolsTracked}, " +
                                  $"stale={fh.StaleSymbolCount}",
                    },
                    new DependencyHealth
                    {
                        Name = "pricing",
                        Status = ss.IsInitialized ? "healthy" : "initializing",
                        LastCheckedAtUtc = DateTimeOffset.UtcNow,
                        Details = ss.IsInitialized
                            ? $"NAV active, {ss.BasisEntries.Count} entries"
                            : "Waiting for bootstrap conditions",
                    },
                    new DependencyHealth
                    {
                        Name = "pending-activation",
                        Status = bs.Pending is null
                            ? "idle"
                            : fh.IsPendingBasketReady ? "ready" : "blocked",
                        LastCheckedAtUtc = DateTimeOffset.UtcNow,
                        Details = bs.Pending is null
                            ? "No pending basket"
                            : fh.IsPendingBasketReady
                                ? "Ready to activate"
                                : pricingEngine.PendingBlockedReason
                                    ?? "Insufficient coverage",
                    },
                    BuildCorporateActionHealth(adjustmentService),
                ],
            };

            return Results.Ok(health);
        })
        .WithName("GetSystemHealth")
        .WithTags("System")
        .WithOpenApi();

        return app;
    }

    private static DependencyHealth BuildCorporateActionHealth(
        ICorporateActionAdjustmentService service)
    {
        var report = service.LastReport;
        if (report is null)
        {
            return new DependencyHealth
            {
                Name = "corporate-actions",
                Status = "idle",
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
                Details = "No adjustment computed yet",
            };
        }

        var symbols = report.Adjustments.Count > 0
            ? string.Join(", ", report.Adjustments.Select(a =>
                $"{a.Symbol}×{a.CumulativeSplitFactor:G}"))
            : "none";

        return new DependencyHealth
        {
            Name = "corporate-actions",
            Status = report.ProviderFailed ? "degraded" : "healthy",
            LastCheckedAtUtc = report.ComputedAtUtc,
            Details = report.ProviderFailed
                ? $"Provider failed: {report.ProviderError}"
                : $"{report.AdjustedConstituentCount} adjusted, " +
                  $"{report.UnadjustedConstituentCount} unchanged " +
                  $"(lag {report.BasketAsOfDate}→{report.RuntimeDate}, " +
                  $"affected: {symbols})",
        };
    }
}
