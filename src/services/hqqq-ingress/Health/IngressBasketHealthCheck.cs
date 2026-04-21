using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Health;

/// <summary>
/// Reports the ingress's view of the active basket on <c>/healthz/ready</c>.
/// The Tiingo subscription is driven by <c>refdata.basket.active.v1</c>;
/// if reference-data isn't publishing, ingress can't know which symbols
/// to subscribe to and the service is operationally degraded even if the
/// websocket itself is "connected" from a previous universe.
/// </summary>
/// <remarks>
/// State machine:
/// <list type="bullet">
///   <item>No basket yet and no override symbols → <c>Unhealthy</c>.</item>
///   <item>No basket yet but bootstrap override is active → <c>Degraded</c>
///         (operator visibility: we're running on a stale override).</item>
///   <item>Basket applied → <c>Healthy</c> (fingerprint / count exposed
///         in the payload).</item>
/// </list>
/// </remarks>
public sealed class IngressBasketHealthCheck : IHealthCheck
{
    private readonly ActiveSymbolUniverse _universe;
    private readonly BasketSubscriptionCoordinator _coordinator;
    private readonly IngressBasketOptions _options;

    public IngressBasketHealthCheck(
        ActiveSymbolUniverse universe,
        BasketSubscriptionCoordinator coordinator,
        IOptions<IngressBasketOptions> options)
    {
        _universe = universe;
        _coordinator = coordinator;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var current = _universe.Current;
        var applied = _coordinator.CurrentAppliedSymbols;
        var appliedFingerprint = _coordinator.AppliedFingerprint;
        var lastAppliedUtc = _coordinator.LastAppliedUtc;

        var data = new Dictionary<string, object>
        {
            ["topic"] = _options.Topic,
            ["appliedSymbolCount"] = applied.Count,
        };
        if (appliedFingerprint is not null) data["appliedFingerprint"] = appliedFingerprint;
        if (lastAppliedUtc is not null) data["lastAppliedUtc"] = lastAppliedUtc.Value.ToString("O");

        if (current is not null)
        {
            data["basketId"] = current.BasketId;
            data["basketFingerprint"] = current.Fingerprint;
            data["basketAsOfDate"] = current.AsOfDate.ToString("yyyy-MM-dd");
            data["basketSource"] = current.Source;
            data["basketConstituentCount"] = current.Symbols.Count;
            data["basketUpdatedUtc"] = current.UpdatedAtUtc.ToString("O");
            data["basketFingerprintAgeSeconds"] = Math.Max(0,
                (DateTimeOffset.UtcNow - current.UpdatedAtUtc).TotalSeconds);
        }

        if (current is null && appliedFingerprint is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"no basket received on {_options.Topic} yet; ingress cannot subscribe",
                data: data));
        }

        if (current is null
            && string.Equals(appliedFingerprint, "bootstrap:override", StringComparison.Ordinal))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "ingress is running on the Tiingo:Symbols bootstrap override — no basket event has arrived yet",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"ingress: basket applied ({applied.Count} symbols, fingerprint {appliedFingerprint})",
            data));
    }
}
