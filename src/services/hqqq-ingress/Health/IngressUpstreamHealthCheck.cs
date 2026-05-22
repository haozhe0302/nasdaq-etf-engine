using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Health;

/// <summary>
/// Reports the live state of the Tiingo upstream connection on
/// <c>/healthz/ready</c>. Phase 2 ingress has a single self-sufficient
/// runtime path with two transports — the IEX websocket (primary) and the
/// REST snapshot fallback (<see cref="Workers.TiingoFallbackLoop"/>) — and
/// the probe reflects whichever one is delivering ticks:
/// <list type="bullet">
///   <item>WS connected + fresh ticks → <see cref="HealthStatus.Healthy"/>.</item>
///   <item>WS connected but no recent tick → <see cref="HealthStatus.Degraded"/>
///         (the socket is up but quiet — the fallback loop will arm and
///         keep prices flowing).</item>
///   <item>WS not connected but fallback active and publishing fresh
///         REST ticks → <see cref="HealthStatus.Degraded"/>. The service is
///         operating in fallback mode; the gateway/UI surfaces this
///         clearly without alarming as fully down.</item>
///   <item>WS not connected AND fallback not publishing fresh ticks →
///         <see cref="HealthStatus.Unhealthy"/> with the last upstream
///         error so operators can see why both transports are down.</item>
/// </list>
/// The structured fields surfaced via <see cref="HealthCheckResult.Data"/>
/// (e.g. <c>webSocketConnected</c>, <c>fallbackActive</c>,
/// <c>lastPublishedTickUtc</c>) are the same names the gateway aggregator
/// projects into the system-health <c>upstream</c> block, so the frontend
/// /system page can render "Fallback Active" without parsing description
/// strings.
/// </summary>
public sealed class IngressUpstreamHealthCheck : IHealthCheck
{
    private readonly IngestionState _state;
    private readonly TiingoOptions _options;
    private readonly TimeProvider _clock;

    public IngressUpstreamHealthCheck(
        IngestionState state,
        IOptions<TiingoOptions> options,
        TimeProvider? clock = null)
    {
        _state = state;
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, _options.StaleAfterSeconds));
        // Allow the fallback poll cadence to dictate freshness when it is
        // slower than the WS staleness window so a 15s REST cadence with
        // StaleAfterSeconds=60 still reads as "fresh".
        var publishFreshnessWindow = TimeSpan.FromSeconds(Math.Max(
            staleAfter.TotalSeconds,
            2 * Math.Max(1, _options.RestPollingIntervalSeconds)));

        var lastActivity = _state.LastActivityUtc;
        var lastPublished = _state.LastPublishedTickUtc;
        var wsConnected = _state.IsUpstreamConnected;
        var fallbackActive = _state.IsFallbackActive;

        var wsFresh = lastActivity is { } a && now - a <= staleAfter;
        var publishedFresh = lastPublished is { } p && now - p <= publishFreshnessWindow;

        var data = new Dictionary<string, object>
        {
            ["webSocketConnected"] = wsConnected,
            ["isUpstreamConnected"] = wsConnected,
            ["fallbackActive"] = fallbackActive,
            ["ticksIngested"] = _state.TicksIngested,
            ["publishedTickCount"] = _state.PublishedTickCount,
            ["fallbackPollSuccessCount"] = _state.FallbackPollSuccessCount,
            ["fallbackPollFailureCount"] = _state.FallbackPollFailureCount,
            ["staleAfterSeconds"] = _options.StaleAfterSeconds,
            ["restPollingIntervalSeconds"] = _options.RestPollingIntervalSeconds,
        };

        if (lastActivity is { } la) data["lastDataUtc"] = la;
        if (lastPublished is { } lp) data["lastPublishedTickUtc"] = lp;
        if (_state.LastFallbackPollUtc is { } lf) data["lastFallbackPollUtc"] = lf;
        if (_state.LastError is { } err) data["lastError"] = err;
        if (_state.LastErrorAtUtc is { } errAt) data["lastErrorAtUtc"] = errAt;

        // Path 1: websocket connected and recently delivering.
        if (wsConnected && wsFresh)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "ingress: websocket connected and fresh",
                data));
        }

        // Path 2: websocket connected but quiet — usually a transient
        // pre-tick window. Degraded, not unhealthy.
        if (wsConnected && !wsFresh)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"ingress: websocket connected but no tick observed in {_options.StaleAfterSeconds}s",
                data: data));
        }

        // Path 3: websocket disconnected but the REST fallback is actively
        // publishing fresh ticks. This is the explicit "operating in
        // fallback" health posture — degraded, not unhealthy.
        if (!wsConnected && fallbackActive && publishedFresh)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                _state.LastError is { } e
                    ? $"ingress: websocket disconnected, REST fallback active ({e})"
                    : "ingress: websocket disconnected, REST fallback active and publishing",
                data: data));
        }

        // Path 4: everything we know about is down or stale — surface
        // the last error so operators can see why.
        return Task.FromResult(HealthCheckResult.Unhealthy(
            _state.LastError ?? "Tiingo websocket not connected and REST fallback not delivering",
            data: data));
    }
}
