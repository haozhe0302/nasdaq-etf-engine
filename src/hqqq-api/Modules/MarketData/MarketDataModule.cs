using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.MarketData.Services;

namespace Hqqq.Api.Modules.MarketData;

public static class MarketDataModule
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<MarketSessionService>();
        services.AddSingleton<SubscriptionManager>();

        services.AddSingleton<InMemoryLatestPriceStore>();
        services.AddSingleton<ILatestPriceStore>(sp =>
            sp.GetRequiredService<InMemoryLatestPriceStore>());

        services.AddSingleton<TiingoWebSocketClient>();
        services.AddSingleton<TiingoRestClient>();

        services.AddSingleton<MarketDataIngestionService>();
        services.AddSingleton<IMarketDataIngestionService>(sp =>
            sp.GetRequiredService<MarketDataIngestionService>());
        services.AddHostedService(sp =>
            sp.GetRequiredService<MarketDataIngestionService>());

        return services;
    }

    public static WebApplication MapMarketDataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/marketdata").WithTags("MarketData");

        group.MapGet("/status", (
            ILatestPriceStore priceStore,
            IMarketDataIngestionService ingestion) =>
        {
            var health = priceStore.GetHealthSnapshot() with
            {
                WebSocketConnected = ingestion.IsWebSocketConnected,
                FallbackActive = ingestion.IsFallbackActive,
            };

            return Results.Ok(new
            {
                isRunning = ingestion.IsRunning,
                lastActivityUtc = ingestion.LastActivityUtc,
                health,
            });
        })
        .WithName("GetMarketDataStatus")
        .WithOpenApi();

        group.MapGet("/latest", (string? symbols, ILatestPriceStore priceStore) =>
        {
            if (string.IsNullOrWhiteSpace(symbols))
                return Results.Ok(priceStore.GetAll());

            var list = symbols.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(priceStore.GetLatest(list));
        })
        .WithName("GetLatestPrices")
        .WithOpenApi();

        // Legacy placeholder aliases kept for backward compatibility
        group.MapGet("/prices", (ILatestPriceStore store) =>
            Results.Ok(store.GetAll()))
            .WithName("GetLatestPricesLegacy")
            .ExcludeFromDescription();

        group.MapGet("/health", (
            ILatestPriceStore store,
            IMarketDataIngestionService ingestion) =>
        {
            return Results.Ok(store.GetHealthSnapshot() with
            {
                WebSocketConnected = ingestion.IsWebSocketConnected,
                FallbackActive = ingestion.IsFallbackActive,
            });
        })
        .WithName("GetFeedHealthLegacy")
        .ExcludeFromDescription();

        return app;
    }
}
