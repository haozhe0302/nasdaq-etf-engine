using Confluent.Kafka;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Minimal helpers to build Confluent.Kafka producer/consumer/admin
/// configs from shared <see cref="KafkaOptions"/>. When the options
/// carry SASL/SSL settings (e.g. for Azure Event Hubs Kafka) those
/// are applied here so every Kafka client in the codebase picks them
/// up uniformly. When auth fields are absent, behaviour is identical
/// to a Plaintext local-dev broker.
/// </summary>
public static class KafkaConfigBuilder
{
    public static ProducerConfig BuildProducerConfig(KafkaOptions options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true,
        };
        ApplySecurity(config, options);
        return config;
    }

    public static ConsumerConfig BuildConsumerConfig(KafkaOptions options, string serviceName)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            GroupId = $"{options.ConsumerGroupPrefix}-{serviceName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };
        ApplySecurity(config, options);
        return config;
    }

    public static AdminClientConfig BuildAdminConfig(KafkaOptions options, int? socketTimeoutMs = null)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
        };
        if (socketTimeoutMs is int timeout)
        {
            config.SocketTimeoutMs = timeout;
        }
        ApplySecurity(config, options);
        return config;
    }

    private static void ApplySecurity(ClientConfig config, KafkaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecurityProtocol))
        {
            // No auth configured — leave defaults (Plaintext) untouched
            // so local docker-compose behaviour is byte-identical.
            return;
        }

        config.SecurityProtocol = Enum.Parse<SecurityProtocol>(options.SecurityProtocol, ignoreCase: true);

        if (!string.IsNullOrWhiteSpace(options.SaslMechanism))
        {
            config.SaslMechanism = Enum.Parse<SaslMechanism>(options.SaslMechanism, ignoreCase: true);
        }
        if (options.SaslUsername is not null)
        {
            config.SaslUsername = options.SaslUsername;
        }
        if (options.SaslPassword is not null)
        {
            config.SaslPassword = options.SaslPassword;
        }
    }
}
