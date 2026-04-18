using Hqqq.Gateway.Services.Adapters.Legacy;
using Hqqq.Gateway.Services.Adapters.Stub;
using Hqqq.Gateway.Services.Infrastructure;
using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Services.Timescale;
using Hqqq.Infrastructure.Timescale;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

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

        var globalMode = options.ResolveMode(environment);
        var quoteMode = options.ResolveQuoteMode(environment);
        var constituentsMode = options.ResolveConstituentsMode(environment);
        var historyMode = options.ResolveHistoryMode(environment);

        // System-health stays on the global mode (B1 transitional path) until
        // a later observability step. It does not accept `redis` or `timescale`.
        var systemHealthMode = globalMode;

        services.AddSingleton(new ResolvedGatewayMode(globalMode));
        services.AddSingleton(new ResolvedSourceModes(
            Quote: quoteMode,
            Constituents: constituentsMode,
            History: historyMode,
            SystemHealth: systemHealthMode));

        var anyLegacy =
            quoteMode == GatewayDataSourceMode.Legacy
            || constituentsMode == GatewayDataSourceMode.Legacy
            || historyMode == GatewayDataSourceMode.Legacy
            || systemHealthMode == GatewayDataSourceMode.Legacy;

        if (anyLegacy && !string.IsNullOrWhiteSpace(options.LegacyBaseUrl))
        {
            services.AddHttpClient(LegacyHttpClientName, client =>
            {
                client.BaseAddress = new Uri(options.LegacyBaseUrl!);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
        }

        var anyRedis =
            quoteMode == GatewayDataSourceMode.Redis
            || constituentsMode == GatewayDataSourceMode.Redis;

        if (anyRedis)
        {
            services.AddSingleton<IGatewayRedisReader, GatewayRedisReader>();
        }

        if (historyMode == GatewayDataSourceMode.Timescale)
        {
            // Shared NpgsqlDataSource for all Timescale readers in the
            // gateway. Mirrors the pattern in hqqq-persistence/Program.cs.
            services.AddSingleton(sp =>
            {
                var timescaleOptions = sp.GetRequiredService<IOptions<TimescaleOptions>>().Value;
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nameof(TimescaleConnectionFactory));
                return TimescaleConnectionFactory.Create(timescaleOptions, logger);
            });
            services.AddSingleton<ITimescaleHistoryQueryService, TimescaleHistoryQueryService>();
        }

        RegisterQuoteSource(services, quoteMode);
        RegisterConstituentsSource(services, constituentsMode);
        RegisterHistorySource(services, historyMode);
        RegisterSystemHealthSource(services, systemHealthMode);

        return services;
    }

    private static void RegisterQuoteSource(IServiceCollection services, GatewayDataSourceMode mode)
    {
        switch (mode)
        {
            case GatewayDataSourceMode.Redis:
                services.AddSingleton<IQuoteSource, RedisQuoteSource>();
                break;
            case GatewayDataSourceMode.Legacy:
                services.AddSingleton<IQuoteSource, LegacyHttpQuoteSource>();
                break;
            default:
                services.AddSingleton<IQuoteSource, StubQuoteSource>();
                break;
        }
    }

    private static void RegisterConstituentsSource(IServiceCollection services, GatewayDataSourceMode mode)
    {
        switch (mode)
        {
            case GatewayDataSourceMode.Redis:
                services.AddSingleton<IConstituentsSource, RedisConstituentsSource>();
                break;
            case GatewayDataSourceMode.Legacy:
                services.AddSingleton<IConstituentsSource, LegacyHttpConstituentsSource>();
                break;
            default:
                services.AddSingleton<IConstituentsSource, StubConstituentsSource>();
                break;
        }
    }

    private static void RegisterHistorySource(IServiceCollection services, GatewayDataSourceMode mode)
    {
        // History supports stub / legacy / timescale. `redis` is not valid
        // here (the history source selection resolver never emits it).
        switch (mode)
        {
            case GatewayDataSourceMode.Timescale:
                services.AddSingleton<IHistorySource, TimescaleHistorySource>();
                break;
            case GatewayDataSourceMode.Legacy:
                services.AddSingleton<IHistorySource, LegacyHttpHistorySource>();
                break;
            default:
                services.AddSingleton<IHistorySource, StubHistorySource>();
                break;
        }
    }

    private static void RegisterSystemHealthSource(IServiceCollection services, GatewayDataSourceMode mode)
    {
        // System-health stays on stub/legacy only — native aggregation lands
        // in a later observability step.
        switch (mode)
        {
            case GatewayDataSourceMode.Legacy:
                services.AddSingleton<ISystemHealthSource, LegacyHttpSystemHealthSource>();
                break;
            default:
                services.AddSingleton<ISystemHealthSource, StubSystemHealthSource>();
                break;
        }
    }
}
