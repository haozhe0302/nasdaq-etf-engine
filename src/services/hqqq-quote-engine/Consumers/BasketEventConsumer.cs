using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Consumers;

/// <summary>
/// Consumes <see cref="BasketActiveStateV1"/> messages from the compacted
/// <c>refdata.basket.active.v1</c> topic, translates them into the engine's
/// fully-materialized <see cref="ActiveBasket"/>, and publishes them into
/// the in-process basket sink that the pipeline worker drains.
/// </summary>
/// <remarks>
/// Version-aware: a repeat of the same fingerprint (common after a compacted
/// topic replays on restart) is logged and skipped so the engine never
/// blends multiple basket versions. A genuinely new fingerprint always
/// replaces the active basket — the engine core's <c>OnBasketActivated</c>
/// handles the series-buffer reset.
/// </remarks>
public sealed class BasketEventConsumer : BackgroundService
{
    private readonly IOptions<KafkaOptions> _kafkaOptions;
    private readonly QuoteEngineOptions _engineOptions;
    private readonly IBasketStateSink _sink;
    private readonly ILogger<BasketEventConsumer> _logger;

    private string? _lastAppliedFingerprint;

    public BasketEventConsumer(
        IOptions<KafkaOptions> kafkaOptions,
        QuoteEngineOptions engineOptions,
        IBasketStateSink sink,
        ILogger<BasketEventConsumer> logger)
    {
        _kafkaOptions = kafkaOptions;
        _engineOptions = engineOptions;
        _sink = sink;
        _logger = logger;
    }

    /// <summary>Last fingerprint actually forwarded to the sink, or null if none yet.</summary>
    public string? LastAppliedFingerprint => _lastAppliedFingerprint;

    /// <summary>
    /// Primes the idempotency guard after a checkpoint restore so a replay
    /// of the same fingerprint from Kafka does not re-publish the basket.
    /// </summary>
    public void PrimeFromRestoredFingerprint(string fingerprint)
    {
        if (!string.IsNullOrEmpty(fingerprint))
            _lastAppliedFingerprint = fingerprint;
    }

    /// <summary>
    /// Validates and publishes a single decoded active-basket state event.
    /// Public so tests can exercise mapping + idempotency without a broker.
    /// </summary>
    public async ValueTask<bool> HandleAsync(BasketActiveStateV1? value, CancellationToken ct)
    {
        if (value is null)
        {
            _logger.LogDebug("Skipping null/tombstone basket message");
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.BasketId) || string.IsNullOrWhiteSpace(value.Fingerprint))
        {
            _logger.LogWarning("Dropping BasketActiveStateV1 with missing basketId / fingerprint");
            return false;
        }

        if (value.ScaleFactor <= 0m)
        {
            _logger.LogWarning(
                "Dropping BasketActiveStateV1 {BasketId} fp={Fingerprint} with non-positive scaleFactor {Scale}",
                value.BasketId, value.Fingerprint, value.ScaleFactor);
            return false;
        }

        if (value.Constituents.Count == 0 || value.PricingBasis.Entries.Count == 0)
        {
            _logger.LogWarning(
                "Dropping BasketActiveStateV1 {BasketId} fp={Fingerprint} with empty constituents/basis",
                value.BasketId, value.Fingerprint);
            return false;
        }

        if (string.Equals(_lastAppliedFingerprint, value.Fingerprint, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "basket-idempotent: fp={Fingerprint} already applied — skipping replay",
                value.Fingerprint);
            return false;
        }

        ActiveBasket basket;
        try
        {
            basket = ActiveBasketMapper.ToActiveBasket(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to map BasketActiveStateV1 {BasketId} fp={Fingerprint} — skipping",
                value.BasketId, value.Fingerprint);
            return false;
        }

        await _sink.PublishAsync(basket, ct).ConfigureAwait(false);
        _lastAppliedFingerprint = value.Fingerprint;

        _logger.LogInformation(
            "Basket activation published: {BasketId} fp={Fingerprint} version={Version} constituents={Count} scale={Scale:E4}",
            basket.BasketId, basket.Fingerprint, value.Version,
            basket.Constituents.Count, basket.ScaleFactor.Value);

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = KafkaConfigBuilder.BuildConsumerConfig(_kafkaOptions.Value, "quote-engine-baskets");
        // Compacted topic: always read from earliest so a fresh consumer
        // hydrates from the full compacted history rather than only new events.
        config.AutoOffsetReset = AutoOffsetReset.Earliest;

        using var consumer = new ConsumerBuilder<string, BasketActiveStateV1?>(config)
            .SetValueDeserializer(new JsonValueDeserializer<BasketActiveStateV1>())
            .SetErrorHandler((_, err) =>
                _logger.LogWarning("Kafka basket consumer error: {Reason} (code={Code})",
                    err.Reason, err.Code))
            .Build();

        try
        {
            consumer.Subscribe(_engineOptions.BasketActiveTopic);
            _logger.LogInformation(
                "BasketEventConsumer subscribed to {Topic} as group {Group}",
                _engineOptions.BasketActiveTopic, config.GroupId);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, BasketActiveStateV1?>? result = null;
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
                        _engineOptions.BasketActiveTopic);
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
                _logger.LogDebug(ex, "BasketEventConsumer close failed");
            }
            _logger.LogInformation("BasketEventConsumer stopped");
        }
    }
}
