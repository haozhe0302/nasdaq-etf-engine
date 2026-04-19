namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Kafka connection settings, bound to the "Kafka" configuration section.
/// </summary>
public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ClientId { get; set; } = "hqqq-local";
    public string ConsumerGroupPrefix { get; set; } = "hqqq";
    public string? SchemaRegistryUrl { get; set; }

    // Optional SASL/SSL auth. When SecurityProtocol is null/empty the
    // builders leave Confluent.Kafka at its Plaintext default, which
    // preserves byte-for-byte the existing local/dev behaviour.
    public string? SecurityProtocol { get; set; }
    public string? SaslMechanism { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    /// <summary>
    /// When true (default), <see cref="KafkaBootstrap"/> attempts to
    /// create missing topics via the admin API. Set to false when
    /// targeting a managed broker (e.g. Azure Event Hubs Kafka) where
    /// topic provisioning is owned externally; bootstrap then becomes
    /// a metadata-only validation that warns on missing topics.
    /// </summary>
    public bool EnableTopicBootstrap { get; set; } = true;
}
