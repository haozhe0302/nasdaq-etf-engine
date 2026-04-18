namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Describes a Kafka topic's desired configuration for bootstrap.
/// </summary>
public sealed record KafkaTopicMetadata(
    string Name,
    int Partitions = 1,
    short ReplicationFactor = 1,
    bool Compacted = false);

/// <summary>
/// Central registry of all topic metadata for deterministic bootstrap.
/// </summary>
public static class KafkaTopicRegistry
{
    public static IReadOnlyList<KafkaTopicMetadata> All { get; } =
    [
        new(KafkaTopics.RawTicks, Partitions: 3),
        new(KafkaTopics.LatestBySymbol, Partitions: 3, Compacted: true),
        new(KafkaTopics.BasketActive, Compacted: true),
        new(KafkaTopics.BasketEvents),
        new(KafkaTopics.PricingSnapshots, Partitions: 1),
        new(KafkaTopics.Incidents),
    ];
}
