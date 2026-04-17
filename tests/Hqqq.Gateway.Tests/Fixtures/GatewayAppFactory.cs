using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Infrastructure;
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
    private readonly Dictionary<string, string?> _config = new();
    private FakeHttpMessageHandler? _fakeHandler;
    private FakeGatewayRedisReader? _fakeRedisReader;

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

            if (_fakeRedisReader is not null)
            {
                // Last-wins for singletons — ensure test's fake is resolved
                // even when AddGatewaySources also registered the real reader.
                services.AddSingleton<IGatewayRedisReader>(_fakeRedisReader);
            }
        });
    }
}
