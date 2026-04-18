using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Consumers;

/// <summary>
/// Consumes <see cref="RawTickV1"/> messages from <c>market.raw_ticks.v1</c>,
/// validates them, maps them to the engine-internal <see cref="NormalizedTick"/>,
/// and publishes them into the existing in-process sink channel that the
/// pipeline worker drains.
/// </summary>
/// <remarks>
/// The channel indirection preserves backpressure, keeps the worker's pumping
/// model unchanged, and lets tests drive <see cref="HandleAsync"/> directly
/// without a live broker.
/// </remarks>
public sealed class RawTickConsumer : BackgroundService
{
    private readonly IOptions<KafkaOptions> _kafkaOptions;
    private readonly QuoteEngineOptions _engineOptions;
    private readonly IRawTickSink _sink;
    private readonly ILogger<RawTickConsumer> _logger;

    public RawTickConsumer(
        IOptions<KafkaOptions> kafkaOptions,
        QuoteEngineOptions engineOptions,
        IRawTickSink sink,
        ILogger<RawTickConsumer> logger)
    {
        _kafkaOptions = kafkaOptions;
        _engineOptions = engineOptions;
        _sink = sink;
        _logger = logger;
    }

    /// <summary>
    /// Validates and publishes a single decoded tick. Public so tests can
    /// exercise the mapping without standing up a broker.
    /// </summary>
    public async ValueTask<bool> HandleAsync(RawTickV1? value, CancellationToken ct)
    {
        if (value is null)
        {
            _logger.LogDebug("Skipping null/tombstone tick message");
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

        var tick = new NormalizedTick
        {
            Symbol = value.Symbol,
            Last = value.Last,
            Bid = value.Bid,
            Ask = value.Ask,
            Currency = value.Currency,
            Provider = value.Provider,
            ProviderTimestamp = value.ProviderTimestamp,
            IngressTimestamp = value.IngressTimestamp,
            Sequence = value.Sequence,
        };

        await _sink.PublishAsync(tick, ct).ConfigureAwait(false);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = KafkaConfigBuilder.BuildConsumerConfig(_kafkaOptions.Value, "quote-engine-ticks");

        using var consumer = new ConsumerBuilder<string, RawTickV1?>(config)
            .SetValueDeserializer(new JsonValueDeserializer<RawTickV1>())
            .SetErrorHandler((_, err) =>
                _logger.LogWarning("Kafka tick consumer error: {Reason} (code={Code})",
                    err.Reason, err.Code))
            .Build();

        try
        {
            consumer.Subscribe(_engineOptions.RawTicksTopic);
            _logger.LogInformation(
                "RawTickConsumer subscribed to {Topic} as group {Group}",
                _engineOptions.RawTicksTopic, config.GroupId);

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
                        _engineOptions.RawTicksTopic);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                    continue;

                await HandleAsync(result.Message?.Value, stoppingToken).ConfigureAwait(false);

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
