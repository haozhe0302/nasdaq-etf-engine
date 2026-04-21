using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Health;

/// <summary>
/// Reports the live state of the Tiingo upstream connection on
/// <c>/healthz/ready</c>. Phase 2 ingress has a single self-sufficient
/// runtime path, so the probe reflects real upstream behaviour: not
/// connected → degraded; connected but stale → degraded; connected +
/// fresh → healthy.
/// </summary>
public sealed class IngressUpstreamHealthCheck : IHealthCheck
{
    private readonly IngestionState _state;
    private readonly TiingoOptions _options;

    public IngressUpstreamHealthCheck(
        IngestionState state,
        IOptions<TiingoOptions> options)
    {
        _state = state;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["isUpstreamConnected"] = _state.IsUpstreamConnected,
            ["ticksIngested"] = _state.TicksIngested,
            ["publishedTickCount"] = _state.PublishedTickCount,
            ["staleAfterSeconds"] = _options.StaleAfterSeconds,
        };

        if (_state.LastActivityUtc is { } last) data["lastDataUtc"] = last;
        if (_state.LastPublishedTickUtc is { } pub) data["lastPublishedTickUtc"] = pub;
        if (_state.LastError is { } err) data["lastError"] = err;
        if (_state.LastErrorAtUtc is { } errAt) data["lastErrorAtUtc"] = errAt;

        if (!_state.IsUpstreamConnected)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                _state.LastError ?? "Tiingo websocket not connected",
                data: data));
        }

        if (_state.LastActivityUtc is { } lastActivity
            && DateTimeOffset.UtcNow - lastActivity > TimeSpan.FromSeconds(_options.StaleAfterSeconds))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"No tick observed in {_options.StaleAfterSeconds}s",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "ingress: upstream connected",
            data));
    }
}
