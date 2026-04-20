using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Publishing;

/// <summary>
/// Stub <see cref="ITickPublisher"/> used in
/// <see cref="Hqqq.Infrastructure.Hosting.OperatingMode.Hybrid"/>. Logs
/// instead of producing to Kafka because the legacy monolith owns tick
/// publishing in that posture; double-publishing would split-brain the
/// downstream consumers.
/// </summary>
public sealed class LoggingTickPublisher(ILogger<LoggingTickPublisher> logger) : ITickPublisher
{
    public Task PublishAsync(RawTickV1 tick, CancellationToken ct)
    {
        logger.LogDebug("Tick: {Symbol} @ {Price} (hybrid stub — not published)", tick.Symbol, tick.Last);
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct)
    {
        var count = ticks.TryGetNonEnumeratedCount(out var c) ? c : ticks.Count();
        logger.LogInformation("Hybrid stub: dropped batch of {Count} ticks (monolith publishes)", count);
        return Task.CompletedTask;
    }
}
