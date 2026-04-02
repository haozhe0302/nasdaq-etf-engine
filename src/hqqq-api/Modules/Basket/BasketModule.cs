using Hqqq.Api.Configuration;

namespace Hqqq.Api.Modules.Basket;

public static class BasketModule
{
    public static IServiceCollection AddBasketModule(this IServiceCollection services)
    {
        // IBasketSnapshotProvider implementation will be registered here
        // once the Invesco holdings fetcher is built in a later phase.
        return services;
    }

    public static WebApplication MapBasketEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/basket").WithTags("Basket");

        group.MapGet("/constituents", () =>
        {
            // Placeholder — will delegate to IBasketSnapshotProvider
            return Results.Ok(new { message = "Basket constituents endpoint reserved" });
        })
        .WithName("GetBasketConstituents")
        .WithOpenApi();

        return app;
    }
}
