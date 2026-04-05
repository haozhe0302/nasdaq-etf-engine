using Hqqq.Api.Modules.System.Contracts;
using Hqqq.Api.Modules.System.Services;
using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.Pricing.Services;

namespace Hqqq.Api.Modules.System;

public static class SystemModule
{
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
            MetricsService metrics) =>
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
                Version = typeof(SystemModule).Assembly
                    .GetName().Version?.ToString() ?? "0.0.0",
                Runtime = RuntimeInfo.Capture(),
                Metrics = metrics.GetSnapshot(),
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
                ],
            };

            return Results.Ok(health);
        })
        .WithName("GetSystemHealth")
        .WithTags("System")
        .WithOpenApi();

        app.MapGet("/api/system/ping", () =>
            Results.Ok(new { serverUtc = DateTimeOffset.UtcNow }))
        .WithName("Ping")
        .WithTags("System")
        .WithOpenApi();

        return app;
    }
}
