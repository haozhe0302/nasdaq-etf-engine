using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Publishing;

/// <summary>
/// Stub publisher that logs ticks instead of producing to Kafka.
/// Will be replaced by a real Kafka producer in a later phase.
/// </summary>
public sealed class LoggingTickPublisher(ILogger<LoggingTickPublisher> logger) : ITickPublisher
{
    public Task PublishAsync(RawTickV1 tick, CancellationToken ct)
    {
        logger.LogDebug("Tick: {Symbol} @ {Price}", tick.Symbol, tick.Last);
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct)
    {
        var count = ticks.TryGetNonEnumeratedCount(out var c) ? c : ticks.Count();
        logger.LogInformation("Publishing batch of {Count} ticks (stub — no Kafka)", count);
        return Task.CompletedTask;
    }
}
