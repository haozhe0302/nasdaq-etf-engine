using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Idempotent Kafka topic bootstrap helper.
/// Creates topics only if they do not already exist and applies
/// compaction policy where specified. When the supplied
/// <see cref="KafkaOptions.EnableTopicBootstrap"/> is false (e.g.
/// against a managed broker like Azure Event Hubs Kafka where topic
/// provisioning is owned externally) this becomes a metadata-only
/// validation that warns when expected topics are missing.
/// </summary>
public static class KafkaBootstrap
{
    public static async Task EnsureTopicsAsync(
        KafkaOptions options,
        IReadOnlyList<KafkaTopicMetadata>? topics = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        topics ??= KafkaTopicRegistry.All;

        using var admin = new AdminClientBuilder(
            KafkaConfigBuilder.BuildAdminConfig(options)).Build();

        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
        var existing = metadata.Topics
            .Select(t => t.Topic)
            .ToHashSet(StringComparer.Ordinal);

        if (!options.EnableTopicBootstrap)
        {
            foreach (var topic in topics)
            {
                if (existing.Contains(topic.Name))
                {
                    logger?.LogInformation(
                        "Topic {Topic} present (bootstrap disabled — validation only)",
                        topic.Name);
                }
                else
                {
                    logger?.LogWarning(
                        "Topic {Topic} missing — provisioning is owned externally; expected pre-created",
                        topic.Name);
                }
            }
            return;
        }

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

    /// <summary>
    /// Backward-compatible overload that takes only a bootstrap-server
    /// string. Forwards to the <see cref="KafkaOptions"/>-based overload
    /// so existing call sites keep compiling.
    /// </summary>
    public static Task EnsureTopicsAsync(
        string bootstrapServers,
        IReadOnlyList<KafkaTopicMetadata>? topics = null,
        ILogger? logger = null,
        CancellationToken ct = default)
        => EnsureTopicsAsync(
            new KafkaOptions { BootstrapServers = bootstrapServers },
            topics,
            logger,
            ct);
}
