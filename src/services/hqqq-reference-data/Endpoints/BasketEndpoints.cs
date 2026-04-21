using Hqqq.ReferenceData.Models;
using Hqqq.ReferenceData.Services;

namespace Hqqq.ReferenceData.Endpoints;

/// <summary>
/// Real basket REST surface. Backed by <see cref="IBasketService"/> which
/// projects <see cref="ActiveBasketStore"/> and routes refresh requests to
/// <see cref="BasketRefreshPipeline"/>. Both endpoints return the full
/// payload (no stub / 501 paths) in both operating modes.
/// </summary>
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
                Source = result.Source,
                AsOfDate = result.AsOfDate,
                ActivatedAtUtc = result.ActivatedAtUtc,
                PublishStatus = BasketPublishStatusDto.FromDomain(result.PublishStatus),
                AdjustmentSummary = result.LatestAdjustmentReport is null
                    ? null
                    : AdjustmentSummaryDto.FromDomain(result.LatestAdjustmentReport),
                PreviousBasketId = result.PreviousBasketId,
                PreviousFingerprint = result.PreviousFingerprint,
                Pending = result.Pending is null ? null : PendingBasketDto.FromDomain(result.Pending),
            };
            return Results.Ok(response);
        })
        .WithName("GetCurrentBasket");

        group.MapPost("/refresh", async (IBasketService svc, CancellationToken ct) =>
        {
            var result = await svc.RefreshAsync(ct);
            if (!result.Success)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        changed = false,
                        source = result.Source,
                        error = result.Error,
                        constituentCount = result.ConstituentCount,
                    },
                    statusCode: 500);
            }

            return Results.Ok(new
            {
                status = result.Changed ? "changed" : "unchanged",
                changed = result.Changed,
                source = result.Source,
                fingerprint = result.Fingerprint,
                previousFingerprint = result.PreviousFingerprint,
                constituentCount = result.ConstituentCount,
                asOfDate = result.AsOfDate?.ToString("yyyy-MM-dd"),
            });
        })
        .WithName("RefreshBasket");

        return app;
    }
}
