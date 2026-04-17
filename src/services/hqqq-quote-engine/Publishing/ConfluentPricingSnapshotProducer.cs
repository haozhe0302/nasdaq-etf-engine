using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Production <see cref="IPricingSnapshotProducer"/> backed by a single
/// long-lived Confluent Kafka <see cref="IProducer{TKey, TValue}"/>.
/// Serialization uses the shared <see cref="JsonValueSerializer{T}"/> so the
/// wire format matches every other HQQQ topic.
/// </summary>
public sealed class ConfluentPricingSnapshotProducer : IPricingSnapshotProducer, IDisposable
{
    private readonly IProducer<string, QuoteSnapshotV1> _producer;
    private bool _disposed;

    public ConfluentPricingSnapshotProducer(IOptions<KafkaOptions> kafkaOptions)
    {
        var config = KafkaConfigBuilder.BuildProducerConfig(kafkaOptions.Value);
        _producer = new ProducerBuilder<string, QuoteSnapshotV1>(config)
            .SetValueSerializer(new JsonValueSerializer<QuoteSnapshotV1>())
            .Build();
    }

    public async Task PublishAsync(string topic, string key, QuoteSnapshotV1 value, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var message = new Message<string, QuoteSnapshotV1>
        {
            Key = key,
            Value = value,
            Timestamp = new Timestamp(value.Timestamp.UtcDateTime, TimestampType.CreateTime),
        };

        await _producer.ProduceAsync(topic, message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (KafkaException)
        {
            // Best-effort flush on shutdown — swallowing a transient broker
            // error here is preferable to masking the original shutdown path.
        }

        _producer.Dispose();
    }
}
