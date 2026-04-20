using Hqqq.Infrastructure.Hosting;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Health;

/// <summary>
/// Reports the live state of the Tiingo upstream connection on
/// <c>/healthz/ready</c>. Reports posture without crashing the process:
/// in <see cref="OperatingMode.Hybrid"/> always healthy (the legacy
/// monolith bridges ticks); in <see cref="OperatingMode.Standalone"/>
/// healthy only when connected and we've observed a tick within
/// <see cref="TiingoOptions.StaleAfterSeconds"/>.
/// </summary>
public sealed class IngressUpstreamHealthCheck : IHealthCheck
{
    private readonly IngestionState _state;
    private readonly OperatingModeOptions _mode;
    private readonly TiingoOptions _options;

    public IngressUpstreamHealthCheck(
        IngestionState state,
        OperatingModeOptions mode,
        IOptions<TiingoOptions> options)
    {
        _state = state;
        _mode = mode;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["operatingMode"] = _mode.Mode.ToString().ToLowerInvariant(),
            ["isUpstreamConnected"] = _state.IsUpstreamConnected,
            ["ticksIngested"] = _state.TicksIngested,
        };

        if (_state.LastActivityUtc is { } last) data["lastDataUtc"] = last;
        if (_state.LastError is { } err) data["lastError"] = err;
        if (_state.LastErrorAtUtc is { } errAt) data["lastErrorAtUtc"] = errAt;

        if (_mode.IsHybrid)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "ingress-stub: hybrid mode (legacy monolith bridges ticks)",
                data));
        }

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
