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
    public async Task NotConnected_AndFallbackInactive_IsUnhealthyAndExposesLastError()
    {
        var check = BuildCheck(state =>
        {
            state.RecordError("ws handshake failed");
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Without a working fallback the upstream is fully down, so the
        // probe should escalate to Unhealthy — Degraded would underplay
        // the outage to the gateway aggregator.
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(false, result.Data["webSocketConnected"]);
        Assert.Equal(false, result.Data["fallbackActive"]);
        Assert.Equal("ws handshake failed", result.Data["lastError"]);
        Assert.Contains("ws handshake failed", result.Description);
    }

    [Fact]
    public async Task NotConnected_ButFallbackActiveWithFreshPublish_IsDegradedNotUnhealthy()
    {
        // Operating-in-fallback contract: when the websocket is down but
        // the REST poller is keeping the pipeline fed, the probe must be
        // Degraded so the gateway/UI surface the fallback state without
        // tripping the system-wide alarm.
        var state = new IngestionState();
        state.SetWebSocketConnected(false);
        state.SetFallbackActive(true);
        state.RecordPublishedTick(); // simulates a successful REST publish
        state.RecordError("ws closed by server");

        var check = new IngressUpstreamHealthCheck(
            state,
            Options.Create(new TiingoOptions { StaleAfterSeconds = 60, RestPollingIntervalSeconds = 15 }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(false, result.Data["webSocketConnected"]);
        Assert.Equal(true, result.Data["fallbackActive"]);
        Assert.True(result.Data.ContainsKey("lastPublishedTickUtc"));
        Assert.Contains("fallback", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotConnected_FallbackActiveButPublishStale_IsUnhealthy()
    {
        // Fallback is armed but Tiingo REST is also failing — the pipeline
        // is dead in the water and the probe must say so clearly.
        var state = new IngestionState();
        state.SetWebSocketConnected(false);
        state.SetFallbackActive(true);
        state.RecordError("REST 503");

        // No RecordPublishedTick → LastPublishedTickUtc is null → not fresh.

        var check = new IngressUpstreamHealthCheck(
            state,
            Options.Create(new TiingoOptions { StaleAfterSeconds = 1, RestPollingIntervalSeconds = 1 }));

        await Task.Delay(20);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(false, result.Data["webSocketConnected"]);
        Assert.Equal(true, result.Data["fallbackActive"]);
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
        // Deterministic staleness: record a tick "now", then advance the
        // clock past StaleAfterSeconds via a fake TimeProvider so the
        // probe sees a stale lastActivity without real sleeping.
        var state = new IngestionState();
        state.SetWebSocketConnected(true);
        state.RecordTick();

        var fakeClock = new FixedTimeProvider(DateTimeOffset.UtcNow.AddSeconds(120));

        var check = new IngressUpstreamHealthCheck(
            state,
            Options.Create(new TiingoOptions { StaleAfterSeconds = 60, RestPollingIntervalSeconds = 15 }),
            fakeClock);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("no tick observed", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
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
