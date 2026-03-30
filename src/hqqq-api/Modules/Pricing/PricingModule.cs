namespace Hqqq.Api.Modules.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        // TODO: Phase B — register iNAV calculation services
        return services;
    }

    public static WebApplication MapPricingEndpoints(this WebApplication app)
    {
        // TODO: Phase B — quote snapshot query/streaming endpoints
        return app;
    }
}
