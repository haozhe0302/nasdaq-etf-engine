using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hqqq.ReferenceData.Health;

/// <summary>
/// Standalone-mode readiness probe. Reports the loaded basket seed
/// metadata (basketId, version, asOfDate, fingerprint, constituent count)
/// as health-check data so <c>/healthz/ready</c> tells operators exactly
/// which deterministic basket the service is broadcasting on
/// <c>refdata.basket.active.v1</c>.
/// </summary>
public sealed class BasketSeedHealthCheck : IHealthCheck
{
    private readonly SeedFileBasketRepository _repository;

    public BasketSeedHealthCheck(SeedFileBasketRepository repository)
    {
        _repository = repository;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var seed = _repository.Seed;
        var data = new Dictionary<string, object>
        {
            ["basketId"] = seed.BasketId,
            ["version"] = seed.Version,
            ["asOfDate"] = seed.AsOfDate.ToString("yyyy-MM-dd"),
            ["fingerprint"] = seed.Fingerprint,
            ["constituentCount"] = seed.Constituents.Count,
            ["scaleFactor"] = seed.ScaleFactor,
            ["source"] = seed.Source,
            ["activatedAtUtc"] = _repository.ActivatedAtUtc.ToString("O"),
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            description: $"Basket seed '{seed.BasketId}' v{seed.Version} ({seed.Constituents.Count} constituents) loaded",
            data: data));
    }
}
