using Hqqq.Contracts.Events;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Services;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// <see cref="ISnapshotEventPublisher"/> implementation that forwards
/// <see cref="QuoteSnapshotV1"/> events to <c>pricing.snapshots.v1</c> via
/// an injected <see cref="IPricingSnapshotProducer"/> seam. The publisher
/// owns topic + key selection so the producer stays a thin transport layer.
/// </summary>
public sealed class SnapshotTopicPublisher : ISnapshotEventPublisher
{
    private readonly IPricingSnapshotProducer _producer;
    private readonly QuoteEngineOptions _options;

    public SnapshotTopicPublisher(IPricingSnapshotProducer producer, QuoteEngineOptions options)
    {
        _producer = producer;
        _options = options;
    }

    public async Task PublishAsync(QuoteSnapshotV1 snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.BasketId))
            throw new ArgumentException("snapshot must have a non-empty BasketId", nameof(snapshot));

        await _producer
            .PublishAsync(_options.PricingSnapshotsTopic, snapshot.BasketId, snapshot, ct)
            .ConfigureAwait(false);
    }
}
