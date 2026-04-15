using Hqqq.ReferenceData.Models;
using Hqqq.ReferenceData.Services;

namespace Hqqq.ReferenceData.Endpoints;

public static class BasketEndpoints
{
    public static WebApplication MapBasketEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/basket").WithTags("Basket");

        group.MapGet("/current", async (IBasketService svc, CancellationToken ct) =>
        {
            var result = await svc.GetCurrentAsync(ct);
            if (result is null)
            {
                return Results.Json(
                    new { status = "unavailable", error = "Basket not yet loaded" },
                    statusCode: 503);
            }

            var response = new BasketCurrentResponse
            {
                Active = BasketVersionDto.FromDomain(result.Active),
                Constituents = result.Constituents,
            };
            return Results.Ok(response);
        })
        .WithName("GetCurrentBasket");

        group.MapPost("/refresh", () =>
        {
            return Results.Json(
                new { status = "not_implemented", message = "Basket refresh is not yet wired (Phase 2B)" },
                statusCode: 501);
        })
        .WithName("RefreshBasket");

        return app;
    }
}
