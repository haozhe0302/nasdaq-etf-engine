using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Persistence.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Consumers;

/// <summary>
/// Consumes <see cref="QuoteSnapshotV1"/> events from
/// <see cref="KafkaTopics.PricingSnapshots"/>, validates them, and pushes
/// them into the in-proc <see cref="IQuoteSnapshotSink"/> that the
/// persistence worker drains in batches.
/// </summary>
/// <remarks>
/// Validation failures and malformed payloads are logged at warning and
/// skipped — the consumer loop never crashes on bad data. Kafka
/// <see cref="ConsumeException"/>s follow the same posture, mirroring
/// <c>RawTickConsumer</c> in the quote-engine.
/// </remarks>
public sealed class QuoteSnapshotConsumer : BackgroundService
{
    private readonly IOptions<KafkaOptions> _kafkaOptions;
    private readonly IQuoteSnapshotSink _sink;
    private readonly ILogger<QuoteSnapshotConsumer> _logger;

    public QuoteSnapshotConsumer(
        IOptions<KafkaOptions> kafkaOptions,
        IQuoteSnapshotSink sink,
        ILogger<QuoteSnapshotConsumer> logger)
    {
        _kafkaOptions = kafkaOptions;
        _sink = sink;
        _logger = logger;
    }

    /// <summary>
    /// Validates and forwards a single decoded snapshot. Public so tests can
    /// exercise the mapping without a live broker. Returns <c>true</c> when
    /// the snapshot was accepted and published to the sink.
    /// </summary>
    public async ValueTask<bool> HandleAsync(QuoteSnapshotV1? value, CancellationToken ct)
    {
        if (value is null)
        {
            _logger.LogDebug("Skipping null/tombstone QuoteSnapshotV1 message");
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.BasketId))
        {
            _logger.LogWarning("Dropping QuoteSnapshotV1 with empty BasketId");
            return false;
        }

        if (value.Timestamp == default)
        {
            _logger.LogWarning(
                "Dropping QuoteSnapshotV1 for basket {BasketId} with default timestamp",
                value.BasketId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.QuoteQuality))
        {
            _logger.LogWarning(
                "Dropping QuoteSnapshotV1 for basket {BasketId} with empty QuoteQuality",
                value.BasketId);
            return false;
        }

        await _sink.PublishAsync(value, ct).ConfigureAwait(false);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = KafkaConfigBuilder.BuildConsumerConfig(_kafkaOptions.Value, "persistence-snapshots");

        using var consumer = new ConsumerBuilder<string, QuoteSnapshotV1?>(config)
            .SetValueDeserializer(new JsonValueDeserializer<QuoteSnapshotV1>())
            .SetErrorHandler((_, err) =>
                _logger.LogWarning("Kafka snapshot consumer error: {Reason} (code={Code})",
                    err.Reason, err.Code))
            .Build();

        try
        {
            consumer.Subscribe(KafkaTopics.PricingSnapshots);
            _logger.LogInformation(
                "QuoteSnapshotConsumer subscribed to {Topic} as group {Group}",
                KafkaTopics.PricingSnapshots, config.GroupId);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, QuoteSnapshotV1?>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex,
                        "Consume failed on {Topic} — skipping message",
                        KafkaTopics.PricingSnapshots);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                    continue;

                try
                {
                    await HandleAsync(result.Message?.Value, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // The sink is a bounded in-proc channel; unexpected errors
                    // (e.g. a transient downstream exception surfacing through
                    // a future sink) must not terminate the consumer loop.
                    _logger.LogError(ex,
                        "QuoteSnapshotConsumer handler failed — skipping message");
                }

                try
                {
                    consumer.StoreOffset(result);
                }
                catch (KafkaException ex)
                {
                    _logger.LogDebug(ex, "StoreOffset failed — will retry on next poll");
                }
            }
        }
        finally
        {
            try { consumer.Close(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "QuoteSnapshotConsumer close failed");
            }
            _logger.LogInformation("QuoteSnapshotConsumer stopped");
        }
    }
}
