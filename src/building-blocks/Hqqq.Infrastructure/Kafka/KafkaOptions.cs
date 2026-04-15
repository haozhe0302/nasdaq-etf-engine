namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Kafka connection settings, bound to the "Kafka" configuration section.
/// </summary>
public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupPrefix { get; set; } = "hqqq";
    public string? SchemaRegistryUrl { get; set; }
}
