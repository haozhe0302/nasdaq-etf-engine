using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Persistence.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Consumers;

/// <summary>
/// Consumes <see cref="RawTickV1"/> events from
/// <see cref="KafkaTopics.RawTicks"/>, validates them, and pushes them into
/// the in-proc <see cref="IRawTickSink"/> that the raw-tick persistence
/// worker drains in batches.
/// </summary>
/// <remarks>
/// Uses consumer group <c>persistence-raw-ticks</c>, distinct from the
/// quote-engine's tick consumer group, so each service reads the topic
/// from its own offset and independently. Validation failures and
/// malformed payloads are logged at warning and skipped — the consumer
/// loop never crashes on bad data, mirroring the posture of
/// <see cref="QuoteSnapshotConsumer"/>.
/// </remarks>
public sealed class RawTickConsumer : BackgroundService
{
    private readonly IOptions<KafkaOptions> _kafkaOptions;
    private readonly IRawTickSink _sink;
    private readonly ILogger<RawTickConsumer> _logger;

    public RawTickConsumer(
        IOptions<KafkaOptions> kafkaOptions,
        IRawTickSink sink,
        ILogger<RawTickConsumer> logger)
    {
        _kafkaOptions = kafkaOptions;
        _sink = sink;
        _logger = logger;
    }

    /// <summary>
    /// Validates and forwards a single decoded tick. Public so tests can
    /// exercise the mapping without a live broker. Returns <c>true</c> when
    /// the tick was accepted and published to the sink.
    /// </summary>
    public async ValueTask<bool> HandleAsync(RawTickV1? value, CancellationToken ct)
    {
        if (value is null)
        {
            _logger.LogDebug("Skipping null/tombstone RawTickV1 message");
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.Symbol))
        {
            _logger.LogWarning("Dropping RawTickV1 with empty Symbol");
            return false;
        }

        if (value.Sequence < 0)
        {
            _logger.LogWarning(
                "Dropping RawTickV1 {Symbol} with negative sequence {Sequence}",
                value.Symbol, value.Sequence);
            return false;
        }

        if (value.ProviderTimestamp == default)
        {
            _logger.LogWarning(
                "Dropping RawTickV1 {Symbol} with default provider timestamp",
                value.Symbol);
            return false;
        }

        if (value.IngressTimestamp == default)
        {
            _logger.LogWarning(
                "Dropping RawTickV1 {Symbol} with default ingress timestamp",
                value.Symbol);
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.Currency))
        {
            _logger.LogWarning(
                "Dropping RawTickV1 {Symbol} with empty Currency",
                value.Symbol);
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.Provider))
        {
            _logger.LogWarning(
                "Dropping RawTickV1 {Symbol} with empty Provider",
                value.Symbol);
            return false;
        }

        await _sink.PublishAsync(value, ct).ConfigureAwait(false);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = KafkaConfigBuilder.BuildConsumerConfig(_kafkaOptions.Value, "persistence-raw-ticks");

        using var consumer = new ConsumerBuilder<string, RawTickV1?>(config)
            .SetValueDeserializer(new JsonValueDeserializer<RawTickV1>())
            .SetErrorHandler((_, err) =>
                _logger.LogWarning("Kafka raw-tick consumer error: {Reason} (code={Code})",
                    err.Reason, err.Code))
            .Build();

        try
        {
            consumer.Subscribe(KafkaTopics.RawTicks);
            _logger.LogInformation(
                "RawTickConsumer subscribed to {Topic} as group {Group}",
                KafkaTopics.RawTicks, config.GroupId);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, RawTickV1?>? result = null;
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
                        KafkaTopics.RawTicks);
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
                    // must not terminate the consumer loop, and must not
                    // propagate to the snapshot pipeline either.
                    _logger.LogError(ex,
                        "RawTickConsumer handler failed — skipping message");
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
                _logger.LogDebug(ex, "RawTickConsumer close failed");
            }
            _logger.LogInformation("RawTickConsumer stopped");
        }
    }
}
