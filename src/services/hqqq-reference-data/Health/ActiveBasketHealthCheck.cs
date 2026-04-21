using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Health;

/// <summary>
/// Readiness probe for <c>hqqq-reference-data</c>. Drives a three-state
/// machine (<c>Healthy</c> / <c>Degraded</c> / <c>Unhealthy</c>) from both
/// <see cref="ActiveBasketStore"/> (is a basket activated at all?) and
/// <see cref="PublishHealthTracker"/> (is the active basket landing on the
/// Kafka topic?). The second dimension is the one the audit flagged — a
/// service whose in-memory basket looks fine but whose downstream topic
/// has been silent for 15 minutes is NOT ready.
///
/// State-machine branches (see <see cref="PublishHealthStateEvaluator"/>
/// for the shared transition table):
/// <list type="bullet">
///   <item><c>NoActiveBasket</c> → Unhealthy (startup refresh pending).</item>
///   <item>Active but never published + within grace → Degraded.</item>
///   <item>Active but never published + grace expired → Unhealthy.</item>
///   <item>Silence since last OK exceeds <c>MaxSilenceSeconds</c> → Unhealthy.</item>
///   <item>Consecutive failures ≥ Unhealthy threshold → Unhealthy.</item>
///   <item>Consecutive failures ≥ Degraded threshold → Degraded.</item>
///   <item>Otherwise → Healthy.</item>
/// </list>
/// Every branch emits the full publish-health payload into
/// <see cref="HealthCheckResult.Data"/> so <c>/healthz/ready</c> surfaces
/// exactly WHY the service degraded. Program.cs then maps
/// <c>Degraded</c>/<c>Unhealthy</c> → HTTP 503 so Kubernetes-style probes
/// actually react to a stalled broker.
/// </summary>
public sealed class ActiveBasketHealthCheck : IHealthCheck
{
    private readonly ActiveBasketStore _store;
    private readonly PublishHealthTracker _publishHealth;
    private readonly PublishHealthOptions _options;
    private readonly TimeProvider _clock;

    public ActiveBasketHealthCheck(
        ActiveBasketStore store,
        PublishHealthTracker publishHealth,
        IOptions<ReferenceDataOptions> options,
        TimeProvider? clock = null)
    {
        _store = store;
        _publishHealth = publishHealth;
        _options = options.Value.PublishHealth;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var current = _store.Current;
        var publish = _publishHealth.Snapshot;
        var now = _clock.GetUtcNow();
        var state = PublishHealthStateEvaluator.Evaluate(current, publish, _options, now);
        var data = BuildData(current, publish, now);

        return Task.FromResult(state switch
        {
            PublishHealthState.NoActiveBasket => HealthCheckResult.Unhealthy(
                description: "no active basket yet — startup refresh pending",
                data: data),
            PublishHealthState.Healthy => HealthCheckResult.Healthy(
                description: $"Active basket '{current!.Snapshot.BasketId}' v{current.Snapshot.Version} ({current.Snapshot.Constituents.Count} constituents, source={current.Snapshot.Source})",
                data: data),
            PublishHealthState.Degraded => HealthCheckResult.Degraded(
                description: DescribeDegraded(current!, publish, now),
                data: data),
            _ => HealthCheckResult.Unhealthy(
                description: DescribeUnhealthy(current!, publish, now),
                data: data),
        });
    }

    private string DescribeDegraded(ActiveBasket current, PublishHealthSnapshot publish, DateTimeOffset now)
    {
        if (publish.LastPublishOkUtc is null)
        {
            var activatedFor = (now - current.ActivatedAtUtc).TotalSeconds;
            return $"active basket not yet published (grace window {_options.FirstActivationGraceSeconds}s; {activatedFor:F0}s elapsed)";
        }
        return $"{publish.ConsecutivePublishFailures} consecutive publish failures (threshold {_options.DegradedAfterConsecutiveFailures})";
    }

    private string DescribeUnhealthy(ActiveBasket current, PublishHealthSnapshot publish, DateTimeOffset now)
    {
        if (publish.LastPublishOkUtc is null)
        {
            return "active basket has never been published and grace window expired";
        }
        var silence = (now - publish.LastPublishOkUtc.Value).TotalSeconds;
        if (_options.MaxSilenceSeconds > 0 && silence > _options.MaxSilenceSeconds)
        {
            return $"no successful publish in {silence:F0}s (MaxSilenceSeconds={_options.MaxSilenceSeconds})";
        }
        return $"{publish.ConsecutivePublishFailures} consecutive publish failures (threshold {_options.UnhealthyAfterConsecutiveFailures})";
    }

    private static Dictionary<string, object> BuildData(
        ActiveBasket? current,
        PublishHealthSnapshot publish,
        DateTimeOffset now)
    {
        var data = new Dictionary<string, object>
        {
            ["consecutivePublishFailures"] = publish.ConsecutivePublishFailures,
            ["lastPublishAttemptUtc"] = publish.LastPublishAttemptUtc?.ToString("O") ?? "",
            ["lastPublishOkUtc"] = publish.LastPublishOkUtc?.ToString("O") ?? "",
            ["lastPublishFailureUtc"] = publish.LastPublishFailureUtc?.ToString("O") ?? "",
            ["lastPublishError"] = publish.LastPublishError ?? "",
            ["lastPublishedFingerprint"] = publish.LastPublishedFingerprint ?? "",
        };

        if (current is not null)
        {
            var snapshot = current.Snapshot;
            data["basketId"] = snapshot.BasketId;
            data["version"] = snapshot.Version;
            data["asOfDate"] = snapshot.AsOfDate.ToString("yyyy-MM-dd");
            data["currentFingerprint"] = current.Fingerprint;
            data["constituentCount"] = snapshot.Constituents.Count;
            data["scaleFactor"] = snapshot.ScaleFactor;
            data["source"] = snapshot.Source;
            data["activatedAtUtc"] = current.ActivatedAtUtc.ToString("O");
            data["currentFingerprintPublished"] =
                publish.LastPublishedFingerprint is not null
                && publish.LastPublishedFingerprint == current.Fingerprint;

            if (publish.LastPublishOkUtc is not null)
            {
                var silence = (now - publish.LastPublishOkUtc.Value).TotalSeconds;
                data["publishOutageSeconds"] = Math.Max(0, silence);
            }
        }

        return data;
    }
}
