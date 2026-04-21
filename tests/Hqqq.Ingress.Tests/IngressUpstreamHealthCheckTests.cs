using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Health;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Asserts the operator-visible <c>/healthz/ready</c> payload shape for
/// the Tiingo upstream check. Phase 2 has a single runtime path, so the
/// probe reflects real upstream state only — there is no "hybrid always
/// healthy" branch.
/// </summary>
public class IngressUpstreamHealthCheckTests
{
    [Fact]
    public async Task NotConnected_IsDegradedAndExposesLastError()
    {
        var check = BuildCheck(state =>
        {
            state.RecordError("ws handshake failed");
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(false, result.Data["isUpstreamConnected"]);
        Assert.Equal("ws handshake failed", result.Data["lastError"]);
        Assert.Contains("ws handshake failed", result.Description);
    }

    [Fact]
    public async Task ConnectedAndFresh_IsHealthy()
    {
        var check = BuildCheck(state =>
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

    [Fact]
    public async Task ConnectedButStale_IsDegraded()
    {
        // Use a very small StaleAfterSeconds so the synthetic
        // "last tick was too long ago" arithmetic fires deterministically
        // without Thread.Sleep.
        var state = new IngestionState();
        state.SetWebSocketConnected(true);
        state.RecordTick();

        // Force the lastActivity tick well into the past by making the
        // check's threshold zero seconds.
        var check = new IngressUpstreamHealthCheck(
            state,
            Options.Create(new TiingoOptions { StaleAfterSeconds = 0 }));

        // Wait a moment so "now - lastActivity" > 0.
        await Task.Delay(20);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("No tick observed", result.Description);
    }

    private static IngressUpstreamHealthCheck BuildCheck(Action<IngestionState> arrange)
    {
        var state = new IngestionState();
        arrange(state);
        return new IngressUpstreamHealthCheck(
            state,
            Options.Create(new TiingoOptions { StaleAfterSeconds = 60 }));
    }
}
