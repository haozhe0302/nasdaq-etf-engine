using Hqqq.ReferenceData.Configuration;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Pure function that translates (<see cref="ActiveBasket"/> +
/// <see cref="PublishHealthSnapshot"/> + <see cref="PublishHealthOptions"/>)
/// into a single state label shared between
/// <see cref="Hqqq.ReferenceData.Health.ActiveBasketHealthCheck"/> and
/// <see cref="BasketService"/>. Extracted so the REST layer and the
/// readiness probe can never drift — the state you see on
/// <c>/api/basket/current</c> is exactly the state driving
/// <c>/healthz/ready</c>.
/// </summary>
public enum PublishHealthState
{
    NoActiveBasket,
    Healthy,
    Degraded,
    Unhealthy,
}

public static class PublishHealthStateEvaluator
{
    public static PublishHealthState Evaluate(
        ActiveBasket? current,
        PublishHealthSnapshot publish,
        PublishHealthOptions options,
        DateTimeOffset now)
    {
        if (current is null) return PublishHealthState.NoActiveBasket;

        if (publish.LastPublishOkUtc is null)
        {
            var activatedFor = (now - current.ActivatedAtUtc).TotalSeconds;
            if (options.FirstActivationGraceSeconds > 0
                && activatedFor <= options.FirstActivationGraceSeconds)
            {
                return PublishHealthState.Degraded;
            }
            return PublishHealthState.Unhealthy;
        }

        var silence = (now - publish.LastPublishOkUtc.Value).TotalSeconds;
        if (options.MaxSilenceSeconds > 0 && silence > options.MaxSilenceSeconds)
        {
            return PublishHealthState.Unhealthy;
        }

        if (options.UnhealthyAfterConsecutiveFailures > 0
            && publish.ConsecutivePublishFailures >= options.UnhealthyAfterConsecutiveFailures)
        {
            return PublishHealthState.Unhealthy;
        }

        if (options.DegradedAfterConsecutiveFailures > 0
            && publish.ConsecutivePublishFailures >= options.DegradedAfterConsecutiveFailures)
        {
            return PublishHealthState.Degraded;
        }

        return PublishHealthState.Healthy;
    }

    public static string ToLowerString(PublishHealthState state) => state switch
    {
        PublishHealthState.Healthy => "healthy",
        PublishHealthState.Degraded => "degraded",
        PublishHealthState.Unhealthy => "unhealthy",
        _ => "no-active-basket",
    };
}
