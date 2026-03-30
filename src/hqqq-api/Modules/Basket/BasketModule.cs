namespace Hqqq.Api.Modules.Basket;

public static class BasketModule
{
    public static IServiceCollection AddBasketModule(this IServiceCollection services)
    {
        // TODO: Phase B — register basket composition services
        return services;
    }

    public static WebApplication MapBasketEndpoints(this WebApplication app)
    {
        // TODO: Phase B — basket composition query/upload endpoints
        return app;
    }
}
