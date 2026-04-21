using Hqqq.Gateway.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Covers the Production fail-fast guard in
/// <see cref="GatewaySourceRegistration"/>: a Phase 2 gateway in
/// Production must never resolve Quote / Constituents / History to the
/// <see cref="GatewayDataSourceMode.Stub"/> adapter — Stub is a
/// development shim and a silent misconfiguration signal.
/// </summary>
public class ProductionStubGuardTests
{
    [Fact]
    public void AddGatewaySources_Production_StubDefaults_Throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddGatewaySources(config, env));
        Assert.Contains("Stub", ex.Message);
        Assert.Contains("Production", ex.Message);
    }

    [Fact]
    public void AddGatewaySources_Development_StubDefaults_Allowed()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = new TestHostEnvironment("Development");

        // Should not throw — Stub is a valid dev/demo default.
        services.AddGatewaySources(config, env);
    }

    [Fact]
    public void AddGatewaySources_Production_RealSources_Allowed()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Sources:Quote"] = "redis",
                ["Gateway:Sources:Constituents"] = "redis",
                ["Gateway:Sources:History"] = "timescale",
            })
            .Build();
        var env = new TestHostEnvironment("Production");

        services.AddGatewaySources(config, env);
    }

    [Fact]
    public void AddGatewaySources_Production_LegacyMode_Allowed()
    {
        // Legacy is an opt-in operator choice (parity testing); the
        // guard is specifically about *Stub* being a silent
        // misconfiguration, not about Legacy.
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:DataSource"] = "legacy",
                ["Gateway:LegacyBaseUrl"] = "http://legacy.test",
                ["Gateway:Sources:SystemHealth"] = "legacy",
            })
            .Build();
        var env = new TestHostEnvironment("Production");

        services.AddGatewaySources(config, env);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string name) { EnvironmentName = name; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "hqqq-gateway-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
