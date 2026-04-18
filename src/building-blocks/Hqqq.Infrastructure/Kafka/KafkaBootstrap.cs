using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Idempotent Kafka topic bootstrap helper.
/// Creates topics only if they do not already exist and applies
/// compaction policy where specified.
/// </summary>
public static class KafkaBootstrap
{
    public static async Task EnsureTopicsAsync(
        string bootstrapServers,
        IReadOnlyList<KafkaTopicMetadata>? topics = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        topics ??= KafkaTopicRegistry.All;

        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
        var existing = metadata.Topics
            .Select(t => t.Topic)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var topic in topics)
        {
            if (existing.Contains(topic.Name))
            {
                logger?.LogInformation("Topic {Topic} already exists — skipping", topic.Name);
                continue;
            }

            var spec = new TopicSpecification
            {
                Name = topic.Name,
                NumPartitions = topic.Partitions,
                ReplicationFactor = topic.ReplicationFactor,
            };

            if (topic.Compacted)
            {
                spec.Configs = new Dictionary<string, string>
                {
                    ["cleanup.policy"] = "compact",
                    ["min.cleanable.dirty.ratio"] = "0.1",
                    ["segment.ms"] = "100",
                };
            }

            try
            {
                await admin.CreateTopicsAsync([spec], new CreateTopicsOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(10),
                    RequestTimeout = TimeSpan.FromSeconds(15),
                });
                logger?.LogInformation("Created topic {Topic} (partitions={Partitions}, compacted={Compacted})",
                    topic.Name, topic.Partitions, topic.Compacted);
            }
            catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                logger?.LogInformation("Topic {Topic} already exists (race) — skipping", topic.Name);
            }
        }
    }
}
