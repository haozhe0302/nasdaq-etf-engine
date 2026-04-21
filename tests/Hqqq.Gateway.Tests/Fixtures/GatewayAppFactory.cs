using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Adapters.Aggregated;
using Hqqq.Gateway.Services.Infrastructure;
using Hqqq.Gateway.Services.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// Reusable WebApplicationFactory with config overrides and optional
/// fake HttpMessageHandler / IGatewayRedisReader injection.
/// Re-calls AddGatewaySources after config overrides so mode resolution
/// picks up test values (last-wins in DI).
/// </summary>
public sealed class GatewayAppFactory : WebApplicationFactory<Program>
{
    // Phase 2-hotfix — realtime is disabled by default in the test host
    // so aggregated-health / legacy-forwarding / mode-precedence tests
    // never accidentally depend on a real Redis pub/sub transport on
    // localhost:6379. Tests that exercise QuoteUpdateSubscriber behavior
    // explicitly re-enable it via WithConfig("Gateway:Realtime:Enabled", "true").
    private readonly Dictionary<string, string?> _config = new()
    {
        ["Gateway:Realtime:Enabled"] = "false",
    };
    private FakeHttpMessageHandler? _fakeHandler;
    private FakeHttpMessageHandler? _fakeHealthHandler;
    private FakeGatewayRedisReader? _fakeRedisReader;
    private ITimescaleHistoryQueryService? _fakeHistoryQuery;
    private IServiceHealthClient? _fakeServiceHealthClient;

    /// <summary>
    /// Ad-hoc service overrides run after all other DI registration so
    /// they win last-wins. Used by Phase 2-hotfix realtime tests to swap
    /// <c>IRedisQuoteUpdateChannel</c> without having to hard-wire every
    /// override into this fixture.
    /// </summary>
    internal List<Action<IServiceCollection>> TestServicesConfigurations { get; }
        = new();

    public GatewayAppFactory WithConfig(string key, string value)
    {
        _config[key] = value;
        return this;
    }

    public GatewayAppFactory WithFakeHandler(FakeHttpMessageHandler handler)
    {
        _fakeHandler = handler;
        return this;
    }

    public GatewayAppFactory WithFakeRedisReader(FakeGatewayRedisReader reader)
    {
        _fakeRedisReader = reader;
        return this;
    }

    public GatewayAppFactory WithFakeHistoryQuery(ITimescaleHistoryQueryService query)
    {
        _fakeHistoryQuery = query;
        return this;
    }

    /// <summary>
    /// Routes the named <c>health-aggregator</c> HttpClient through a fake
    /// handler so the AggregatedSystemHealthSource probes go to a stub
    /// instead of real downstream services. Used by AggregatedSystemHealthTests.
    /// </summary>
    public GatewayAppFactory WithFakeHealthHandler(FakeHttpMessageHandler handler)
    {
        _fakeHealthHandler = handler;
        return this;
    }

    /// <summary>
    /// Replaces the registered <see cref="IServiceHealthClient"/> outright
    /// (last-wins). Useful when a test wants to script per-service responses
    /// without driving them through HTTP plumbing.
    /// </summary>
    public GatewayAppFactory WithFakeServiceHealthClient(IServiceHealthClient client)
    {
        _fakeServiceHealthClient = client;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_config.Count > 0)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(_config!);
            });
        }

        builder.ConfigureServices((ctx, services) =>
        {
            if (_config.Count > 0)
            {
                services.AddGatewaySources(ctx.Configuration, ctx.HostingEnvironment);
            }

            if (_fakeHandler is not null)
            {
                services.AddHttpClient(GatewaySourceRegistration.LegacyHttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _fakeHandler);
            }

            if (_fakeHealthHandler is not null)
            {
                services.AddHttpClient(HttpServiceHealthClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _fakeHealthHandler);
            }

            if (_fakeServiceHealthClient is not null)
            {
                services.AddSingleton(_fakeServiceHealthClient);
            }

            if (_fakeRedisReader is not null)
            {
                // Last-wins for singletons — ensure test's fake is resolved
                // even when AddGatewaySources also registered the real reader.
                services.AddSingleton<IGatewayRedisReader>(_fakeRedisReader);
            }

            if (_fakeHistoryQuery is not null)
            {
                // Last-wins for singletons — ensures the Timescale history
                // source resolves the fake query service in tests without
                // needing a real Timescale connection.
                services.AddSingleton<ITimescaleHistoryQueryService>(_fakeHistoryQuery);
            }

            foreach (var extra in TestServicesConfigurations)
            {
                extra(services);
            }
        });
    }
}
