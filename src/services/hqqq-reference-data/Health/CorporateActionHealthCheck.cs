using Hqqq.ReferenceData.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hqqq.ReferenceData.Health;

/// <summary>
/// Readiness probe for the Phase-2-native corporate-action layer.
/// Reports the lineage of the most recent adjustment pass plus a
/// counters snapshot. The check is always <c>Healthy</c> unless the
/// provider's last run recorded an error (degraded) — corp-action
/// fetches degrade silently on per-symbol failure, so this probe is a
/// hint to operators, not a hard readiness gate.
/// </summary>
public sealed class CorporateActionHealthCheck : IHealthCheck
{
    private readonly ActiveBasketStore _store;

    public CorporateActionHealthCheck(ActiveBasketStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var report = _store.LatestAdjustmentReport;
        if (report is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "corp-actions: no refresh has completed yet"));
        }

        var data = new Dictionary<string, object>
        {
            ["source"] = report.Source,
            ["basketAsOfDate"] = report.BasketAsOfDate.ToString("yyyy-MM-dd"),
            ["runtimeDate"] = report.RuntimeDate.ToString("yyyy-MM-dd"),
            ["appliedAtUtc"] = report.AppliedAtUtc.ToString("O"),
            ["splitsApplied"] = report.SplitsApplied,
            ["renamesApplied"] = report.RenamesApplied,
            ["addedSymbols"] = report.AddedSymbols.Count,
            ["removedSymbols"] = report.RemovedSymbols.Count,
            ["scaleFactorRecalibrated"] = report.ScaleFactorRecalibrated,
        };
        if (report.ProviderError is not null) data["providerError"] = report.ProviderError;
        if (report.PreviousScaleFactor is not null) data["previousScaleFactor"] = report.PreviousScaleFactor.Value;
        if (report.NewScaleFactor is not null) data["newScaleFactor"] = report.NewScaleFactor.Value;

        if (report.ProviderError is not null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"corp-actions: provider {report.Source} degraded ({report.ProviderError})",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"corp-actions: {report.SplitsApplied} split(s), {report.RenamesApplied} rename(s), {report.AddedSymbols.Count}+/{report.RemovedSymbols.Count}- transition (source={report.Source})",
            data));
    }
}
