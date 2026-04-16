using Confluent.Kafka;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Minimal helpers to build Confluent.Kafka producer/consumer configs
/// from shared <see cref="KafkaOptions"/>.
/// </summary>
public static class KafkaConfigBuilder
{
    public static ProducerConfig BuildProducerConfig(KafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ClientId,
        Acks = Acks.All,
        EnableIdempotence = true,
    };

    public static ConsumerConfig BuildConsumerConfig(KafkaOptions options, string serviceName) => new()
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ClientId,
        GroupId = $"{options.ConsumerGroupPrefix}-{serviceName}",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    };
}
