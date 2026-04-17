using Hqqq.Gateway.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// Reusable WebApplicationFactory with config overrides and optional
/// fake HttpMessageHandler injection for the named "legacy" HttpClient.
/// Re-calls AddGatewaySources after config overrides so mode resolution
/// picks up test values (last-wins in DI).
/// </summary>
public sealed class GatewayAppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _config = new();
    private FakeHttpMessageHandler? _fakeHandler;

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
        });
    }
}
