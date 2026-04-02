namespace Hqqq.Api.Modules.MarketData;

public static class MarketDataModule
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services)
    {
        // ILatestPriceStore and IMarketDataIngestionService implementations
        // will be registered here once the Tiingo client is built.
        return services;
    }

    public static WebApplication MapMarketDataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/marketdata").WithTags("MarketData");

        group.MapGet("/prices", () =>
        {
            // Placeholder — will delegate to ILatestPriceStore.GetAll()
            return Results.Ok(new { message = "Latest prices endpoint reserved" });
        })
        .WithName("GetLatestPrices")
        .WithOpenApi();

        group.MapGet("/health", () =>
        {
            // Placeholder — will delegate to ILatestPriceStore.GetHealthSnapshot()
            return Results.Ok(new { message = "Feed health endpoint reserved" });
        })
        .WithName("GetFeedHealth")
        .WithOpenApi();

        return app;
    }
}
