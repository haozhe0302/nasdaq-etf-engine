using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Services.Adapters.Legacy;
using Hqqq.Gateway.Services.Adapters.Stub;
using Microsoft.Extensions.Hosting;
namespace Hqqq.Gateway.Configuration;

public static class GatewaySourceRegistration
{
    public const string LegacyHttpClientName = "legacy";

    public static IServiceCollection AddGatewaySources(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(GatewayOptions.SectionName);
        services.Configure<GatewayOptions>(section);

        var options = new GatewayOptions();
        section.Bind(options);
        var mode = options.ResolveMode(environment);

        services.AddSingleton(new ResolvedGatewayMode(mode));

        if (mode == GatewayDataSourceMode.Legacy)
        {
            services.AddHttpClient(LegacyHttpClientName, client =>
            {
                client.BaseAddress = new Uri(options.LegacyBaseUrl!);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            services.AddSingleton<IQuoteSource, LegacyHttpQuoteSource>();
            services.AddSingleton<IConstituentsSource, LegacyHttpConstituentsSource>();
            services.AddSingleton<IHistorySource, LegacyHttpHistorySource>();
            services.AddSingleton<ISystemHealthSource, LegacyHttpSystemHealthSource>();
        }
        else
        {
            services.AddSingleton<IQuoteSource, StubQuoteSource>();
            services.AddSingleton<IConstituentsSource, StubConstituentsSource>();
            services.AddSingleton<IHistorySource, StubHistorySource>();
            services.AddSingleton<ISystemHealthSource, StubSystemHealthSource>();
        }

        return services;
    }
}
