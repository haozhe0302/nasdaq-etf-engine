using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Publishing;

/// <summary>
/// Production <see cref="IBasketPublisher"/> backed by a long-lived
/// Confluent Kafka producer keyed by basket id. Used in standalone mode
/// to publish the deterministic seed onto <see cref="KafkaTopics.BasketActive"/>
/// so quote-engine activates without needing a REST round-trip.
/// </summary>
public sealed class KafkaBasketPublisher : IBasketPublisher, IDisposable
{
    private readonly IProducer<string, BasketActiveStateV1> _producer;
    private readonly bool _ownsProducer;
    private bool _disposed;

    public KafkaBasketPublisher(IOptions<KafkaOptions> kafkaOptions)
    {
        var config = KafkaConfigBuilder.BuildProducerConfig(kafkaOptions.Value);
        _producer = new ProducerBuilder<string, BasketActiveStateV1>(config)
            .SetValueSerializer(new JsonValueSerializer<BasketActiveStateV1>())
            .Build();
        _ownsProducer = true;
    }

    /// <summary>Test seam: lets unit tests inject an in-memory producer.</summary>
    public KafkaBasketPublisher(IProducer<string, BasketActiveStateV1> producer)
    {
        _producer = producer;
        _ownsProducer = false;
    }

    public async Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var message = new Message<string, BasketActiveStateV1>
        {
            Key = state.BasketId,
            Value = state,
            Timestamp = new Timestamp(state.ActivatedAtUtc.UtcDateTime, TimestampType.CreateTime),
        };

        await _producer.ProduceAsync(KafkaTopics.BasketActive, message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_ownsProducer) return;

        try { _producer.Flush(TimeSpan.FromSeconds(5)); } catch (KafkaException) { }
        _producer.Dispose();
    }
}
