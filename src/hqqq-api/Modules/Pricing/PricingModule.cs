using Hqqq.Api.Modules.Pricing.Contracts;
using Hqqq.Api.Modules.Pricing.Services;

namespace Hqqq.Api.Modules.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddSingleton<IScaleStateStore, JsonScaleStateStore>();

        // IQuoteSnapshotService implementation will be registered here
        // once the iNAV computation engine is built.
        return services;
    }

    public static WebApplication MapPricingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/pricing").WithTags("Pricing");

        group.MapGet("/quote", () =>
        {
            // Placeholder — will delegate to IQuoteSnapshotService.GetLatest()
            return Results.Ok(new { message = "Quote snapshot endpoint reserved" });
        })
        .WithName("GetQuoteSnapshot")
        .WithOpenApi();

        return app;
    }
}
