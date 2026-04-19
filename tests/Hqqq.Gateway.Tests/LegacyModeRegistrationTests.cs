using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Services.Adapters.Legacy;
using Hqqq.Gateway.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests;

public class LegacyModeRegistrationTests : IDisposable
{
    private readonly GatewayAppFactory _factory;

    public LegacyModeRegistrationTests()
    {
        _factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            // Phase 2D1 defaulted system-health to `aggregated`; this fixture
            // verifies the legacy-mode registration so we opt back in
            // explicitly for the system-health source assertion.
            .WithConfig("Gateway:Sources:SystemHealth", "legacy")
            .WithFakeHandler(new FakeHttpMessageHandler());
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void QuoteSource_IsLegacyHttpQuoteSource()
    {
        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IQuoteSource>();
        Assert.IsType<LegacyHttpQuoteSource>(source);
    }

    [Fact]
    public void ConstituentsSource_IsLegacyHttpConstituentsSource()
    {
        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IConstituentsSource>();
        Assert.IsType<LegacyHttpConstituentsSource>(source);
    }

    [Fact]
    public void HistorySource_IsLegacyHttpHistorySource()
    {
        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IHistorySource>();
        Assert.IsType<LegacyHttpHistorySource>(source);
    }

    [Fact]
    public void SystemHealthSource_IsLegacyHttpSystemHealthSource()
    {
        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ISystemHealthSource>();
        Assert.IsType<LegacyHttpSystemHealthSource>(source);
    }
}
