using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Publishing;

/// <summary>
/// Production <see cref="IBasketPublisher"/> backed by a long-lived
/// Confluent Kafka producer keyed by basket id. Publishes to
/// <see cref="KafkaTopics.BasketActive"/> (overridable via
/// <c>ReferenceData:Publish:TopicName</c>) so quote-engine activates
/// without needing a REST round-trip. Runs in both operating modes.
/// </summary>
public sealed class KafkaBasketPublisher : IBasketPublisher, IDisposable
{
    private readonly IProducer<string, BasketActiveStateV1> _producer;
    private readonly bool _ownsProducer;
    private readonly string _topic;
    private bool _disposed;

    public KafkaBasketPublisher(
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<ReferenceDataOptions> refDataOptions)
    {
        var config = KafkaConfigBuilder.BuildProducerConfig(kafkaOptions.Value);
        _producer = new ProducerBuilder<string, BasketActiveStateV1>(config)
            .SetValueSerializer(new JsonValueSerializer<BasketActiveStateV1>())
            .Build();
        _ownsProducer = true;
        _topic = ResolveTopic(refDataOptions.Value);
    }

    /// <summary>Test seam: lets unit tests inject an in-memory producer.</summary>
    public KafkaBasketPublisher(IProducer<string, BasketActiveStateV1> producer, string? topic = null)
    {
        _producer = producer;
        _ownsProducer = false;
        _topic = string.IsNullOrWhiteSpace(topic) ? KafkaTopics.BasketActive : topic;
    }

    public string Topic => _topic;

    public async Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var message = new Message<string, BasketActiveStateV1>
        {
            Key = state.BasketId,
            Value = state,
            Timestamp = new Timestamp(state.ActivatedAtUtc.UtcDateTime, TimestampType.CreateTime),
        };

        await _producer.ProduceAsync(_topic, message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_ownsProducer) return;

        try { _producer.Flush(TimeSpan.FromSeconds(5)); } catch (KafkaException) { }
        _producer.Dispose();
    }

    private static string ResolveTopic(ReferenceDataOptions options)
    {
        var overrideName = options.Publish.TopicName;
        return string.IsNullOrWhiteSpace(overrideName) ? KafkaTopics.BasketActive : overrideName;
    }
}
