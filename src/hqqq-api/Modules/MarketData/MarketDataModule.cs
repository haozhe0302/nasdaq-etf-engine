namespace Hqqq.Api.Modules.MarketData;

public static class MarketDataModule
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services)
    {
        // TODO: Phase B — register market data ingestion services
        return services;
    }

    public static WebApplication MapMarketDataEndpoints(this WebApplication app)
    {
        // TODO: Phase B — market data ingestion/query endpoints
        return app;
    }
}
