using Hqqq.Api.Modules.Pricing.Contracts;
using Hqqq.Api.Modules.Pricing.Services;

namespace Hqqq.Api.Modules.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddSingleton<IScaleStateStore, JsonScaleStateStore>();
        services.AddSingleton<ISeriesStore, JsonSeriesStore>();
        services.AddSingleton<BasketPricingBasisBuilder>();
        services.AddSingleton<PricingEngine>();
        services.AddHostedService<QuoteBroadcastService>();

        return services;
    }

    public static WebApplication MapPricingEndpoints(this WebApplication app)
    {
        // ── Primary frontend endpoints ──────────────────────────

        app.MapGet("/api/quote", (PricingEngine engine) =>
        {
            var quote = engine.ComputeQuote();
            return quote is not null
                ? Results.Ok(quote)
                : Results.Json(new
                {
                    status = "initializing",
                    message = "Pricing engine not yet calibrated",
                    isInitialized = engine.IsInitialized,
                }, statusCode: 503);
        })
        .WithName("GetQuote")
        .WithTags("Pricing")
        .WithOpenApi();

        app.MapGet("/api/constituents", (PricingEngine engine) =>
        {
            var snapshot = engine.ComputeConstituents();
            return snapshot is not null
                ? Results.Ok(snapshot)
                : Results.Json(new
                {
                    status = "initializing",
                    message = "Pricing engine not yet calibrated",
                }, statusCode: 503);
        })
        .WithName("GetConstituents")
        .WithTags("Pricing")
        .WithOpenApi();

        // ── Backward-compatible alias ───────────────────────────

        var group = app.MapGroup("/api/pricing").WithTags("Pricing");

        group.MapGet("/quote", (PricingEngine engine) =>
        {
            var quote = engine.ComputeQuote();
            return quote is not null
                ? Results.Ok(quote)
                : Results.Json(new { status = "initializing" }, statusCode: 503);
        })
        .WithName("GetQuoteSnapshotLegacy")
        .ExcludeFromDescription();

        return app;
    }
}
