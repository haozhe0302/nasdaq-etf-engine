using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Adapters.Aggregated;
using Hqqq.Gateway.Services.Adapters.Legacy;
using Hqqq.Gateway.Services.Adapters.Stub;
using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Locks the precedence rules for <c>Gateway:Sources:History</c> added in
/// Phase 2C2. The global <c>Gateway:DataSource</c> is the fallback
/// (stub/legacy only); per-endpoint history overrides can escalate to
/// <c>timescale</c> or de-escalate back to <c>stub</c> / <c>legacy</c>.
/// Phase 2D1: system-health defaults to native aggregation regardless of
/// the global DataSource (verified inline in each scenario).
/// </summary>
public class HistorySourceSelectionTests
{
    [Fact]
    public void HistoryTimescale_Override_ResolvesTimescaleSource()
    {
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:History", "timescale")
            .WithFakeHistoryQuery(new FakeTimescaleHistoryQueryService());

        // Force initialization of the test server so the service provider
        // is fully built.
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var modes = scope.ServiceProvider.GetRequiredService<ResolvedSourceModes>();

        Assert.Equal(GatewayDataSourceMode.Timescale, modes.History);
        Assert.IsType<TimescaleHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());

        // Phase 2D1 — system-health defaults to native aggregation regardless
        // of the global DataSource.
        Assert.IsType<AggregatedSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }

    [Fact]
    public void HistoryStub_OverrideUnderLegacyGlobal_ResolvesStubSource()
    {
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            .WithConfig("Gateway:Sources:History", "stub");

        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<StubHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());
        // Phase 2D1 — system-health defaults to native aggregation; the global
        // Legacy switch no longer drags it onto the legacy forwarder.
        Assert.IsType<AggregatedSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }

    [Fact]
    public void HistoryEmpty_InheritsLegacyGlobal()
    {
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test");

        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<LegacyHttpHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());
        Assert.IsType<AggregatedSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }

    [Fact]
    public void HistoryEmpty_InheritsStubGlobal()
    {
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub");

        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<StubHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());
        Assert.IsType<AggregatedSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }
}
