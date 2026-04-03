using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.Basket.Services;

namespace Hqqq.Api.Modules.Basket;

public static class BasketModule
{
    public static IServiceCollection AddBasketModule(this IServiceCollection services)
    {
        services.AddHttpClient<StockAnalysisAdapter>();
        services.AddHttpClient<SchwabHoldingsAdapter>();
        services.AddHttpClient<AlphaVantageAdapter>();
        services.AddHttpClient<NasdaqHoldingsAdapter>();
        services.AddSingleton<RawSourceCacheService>();
        services.AddSingleton<BasketCacheService>();
        services.AddSingleton<BasketSnapshotProvider>();
        services.AddSingleton<IBasketSnapshotProvider>(sp =>
            sp.GetRequiredService<BasketSnapshotProvider>());
        services.AddHostedService<BasketRefreshService>();

        return services;
    }

    public static WebApplication MapBasketEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/basket").WithTags("Basket");

        group.MapGet("/current", (BasketSnapshotProvider provider) =>
        {
            var state = provider.GetState();
            if (state.Active is null)
            {
                return Results.Json(new
                {
                    status = "unavailable",
                    error = state.LastError ?? "Basket not yet loaded",
                    hasPending = state.Pending is not null,
                    sourceFetchOutcomes = provider.GetLastFetchOutcomes(),
                }, statusCode: 503);
            }

            return Results.Ok(new
            {
                active = new
                {
                    fingerprint = state.ActiveFingerprint,
                    summary = BasketSummary.FromSnapshot(state.Active),
                    constituents = state.Active.Constituents,
                },
                pending = state.Pending is not null ? new
                {
                    fingerprint = state.PendingFingerprint,
                    summary = BasketSummary.FromSnapshot(state.Pending),
                    effectiveAtUtc = state.PendingEffectiveAtUtc,
                } : null,
                sourceFetchOutcomes = provider.GetLastFetchOutcomes(),
            });
        })
        .WithName("GetCurrentBasket")
        .WithOpenApi();

        group.MapPost("/refresh", async (BasketSnapshotProvider provider, CancellationToken ct) =>
        {
            var before = provider.GetState();

            await provider.RefreshAsync(ct);

            var after = provider.GetState();
            var outcomes = provider.GetLastFetchOutcomes();
            var newPending = before.Pending is null && after.Pending is not null;
            var fingerprintUnchanged = after.ActiveFingerprint == before.ActiveFingerprint
                && after.PendingFingerprint == before.PendingFingerprint;

            if (after.Active is null && after.Pending is null)
            {
                return Results.Json(new
                {
                    status = "failed",
                    error = after.LastError,
                    sourceFetchOutcomes = outcomes,
                    newPendingCreated = false,
                    fingerprintUnchanged = true,
                }, statusCode: 503);
            }

            return Results.Ok(new
            {
                status = "refreshed",
                newPendingCreated = newPending,
                fingerprintUnchanged,
                sourceFetchOutcomes = outcomes,
                active = after.Active is not null ? new
                {
                    fingerprint = after.ActiveFingerprint,
                    summary = BasketSummary.FromSnapshot(after.Active),
                } : null,
                pending = after.Pending is not null ? new
                {
                    fingerprint = after.PendingFingerprint,
                    summary = BasketSummary.FromSnapshot(after.Pending),
                    effectiveAtUtc = after.PendingEffectiveAtUtc,
                } : null,
            });
        })
        .WithName("RefreshBasket")
        .WithOpenApi();

        return app;
    }
}
