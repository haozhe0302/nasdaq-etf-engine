using Hqqq.Infrastructure.Hosting;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Health;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Asserts the operator-visible <c>/healthz/ready</c> payload shape for
/// ingress: hybrid is permissive, standalone reflects upstream state.
/// </summary>
public class IngressUpstreamHealthCheckTests
{
    [Fact]
    public async Task Hybrid_IsAlwaysHealthy_AndExposesMode()
    {
        var check = BuildCheck(OperatingMode.Hybrid, state =>
        {
            // Hybrid never connects; the check should still report healthy.
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("hybrid", result.Data["operatingMode"]);
        Assert.Equal(false, result.Data["isUpstreamConnected"]);
        Assert.Contains("hybrid", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Standalone_NotConnected_IsDegradedAndExposesLastError()
    {
        var check = BuildCheck(OperatingMode.Standalone, state =>
        {
            state.RecordError("ws handshake failed");
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("standalone", result.Data["operatingMode"]);
        Assert.Equal(false, result.Data["isUpstreamConnected"]);
        Assert.Equal("ws handshake failed", result.Data["lastError"]);
        Assert.Contains("ws handshake failed", result.Description);
    }

    [Fact]
    public async Task Standalone_ConnectedAndFresh_IsHealthy()
    {
        var check = BuildCheck(OperatingMode.Standalone, state =>
        {
            state.SetWebSocketConnected(true);
            state.RecordTick();
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(true, result.Data["isUpstreamConnected"]);
        Assert.True((long)result.Data["ticksIngested"] >= 1);
        Assert.True(result.Data.ContainsKey("lastDataUtc"));
    }

    private static IngressUpstreamHealthCheck BuildCheck(
        OperatingMode mode,
        Action<IngestionState> arrange)
    {
        var state = new IngestionState();
        arrange(state);
        return new IngressUpstreamHealthCheck(
            state,
            new OperatingModeOptions { Mode = mode },
            Options.Create(new TiingoOptions { StaleAfterSeconds = 60 }));
    }
}
