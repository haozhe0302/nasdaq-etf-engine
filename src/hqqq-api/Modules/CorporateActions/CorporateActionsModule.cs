using Hqqq.Api.Modules.CorporateActions.Contracts;
using Hqqq.Api.Modules.CorporateActions.Services;

namespace Hqqq.Api.Modules.CorporateActions;

public static class CorporateActionsModule
{
    public static IServiceCollection AddCorporateActionsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICorporateActionProvider, TiingoCorporateActionProvider>();
        services.AddSingleton<ICorporateActionAdjustmentService, CorporateActionAdjustmentService>();

        return services;
    }
}
