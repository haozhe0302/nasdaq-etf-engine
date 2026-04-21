using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Consumers;

/// <summary>
/// Hosted background service that consumes <c>refdata.basket.active.v1</c>
/// (a compacted Kafka topic) and feeds every received
/// <see cref="BasketActiveStateV1"/> into <see cref="ActiveSymbolUniverse"/>.
/// The <see cref="BasketSubscriptionCoordinator"/> reacts to the universe
/// event and drives the Tiingo websocket subscription.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AutoOffsetReset.Earliest"/> so a fresh consumer
/// replays the full compacted history and lands on the currently-active
/// basket without operator action. Offsets are not committed — the
/// compacted topic is authoritative and we always want to re-read from
/// the beginning after a restart.
/// </para>
/// <para>
/// Failures in the consumer loop are caught and retried with a short
/// delay; the rest of the ingress process keeps running (tick stream,
/// health probes, etc.) so an intermittent Kafka outage never takes the
/// service down.
/// </para>
/// </remarks>
public sealed class BasketActiveConsumer : BackgroundService
{
    private readonly ActiveSymbolUniverse _universe;
    private readonly IOptions<KafkaOptions> _kafkaOptions;
    private readonly IngressBasketOptions _options;
    private readonly ILogger<BasketActiveConsumer> _logger;

    public BasketActiveConsumer(
        ActiveSymbolUniverse universe,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<IngressBasketOptions> options,
        ILogger<BasketActiveConsumer> logger)
    {
        _universe = universe;
        _kafkaOptions = kafkaOptions;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BasketActiveConsumer starting — topic={Topic} group={Group}",
            _options.Topic, _options.ConsumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "BasketActiveConsumer: loop failed; retrying in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("BasketActiveConsumer stopping");
    }

    private async Task RunConsumerAsync(CancellationToken ct)
    {
        var config = KafkaConfigBuilder.BuildConsumerConfig(_kafkaOptions.Value, _options.ConsumerGroup);
        config.AutoOffsetReset = AutoOffsetReset.Earliest;

        using var consumer = new ConsumerBuilder<string, BasketActiveStateV1?>(config)
            .SetValueDeserializer(new JsonValueDeserializer<BasketActiveStateV1>())
            .SetErrorHandler((_, err) =>
                _logger.LogWarning("BasketActiveConsumer Kafka error: {Reason}", err.Reason))
            .Build();

        consumer.Subscribe(_options.Topic);

        try
        {
            await Task.Yield();
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, BasketActiveStateV1?>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "BasketActiveConsumer: consume error");
                    continue;
                }

                if (result is null) continue;

                Apply(result.Message?.Value);
            }
        }
        finally
        {
            try { consumer.Close(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Projects a single decoded basket message into
    /// <see cref="ActiveSymbolUniverse"/>. Public surface is intentionally
    /// internal so unit tests can drive the consumer's mapping +
    /// validation logic without standing up a broker. Returns
    /// <c>true</c> when a basket was applied; <c>false</c> for
    /// tombstones / malformed payloads (each rejected with a structured
    /// log).
    /// </summary>
    internal bool Apply(BasketActiveStateV1? basket)
    {
        if (basket is null)
        {
            _logger.LogDebug("BasketActiveConsumer: skipping null/tombstone basket message");
            return false;
        }

        if (string.IsNullOrWhiteSpace(basket.BasketId)
            || string.IsNullOrWhiteSpace(basket.Fingerprint))
        {
            _logger.LogWarning(
                "BasketActiveConsumer: dropping basket with missing basketId/fingerprint (constituents={Count})",
                basket.Constituents?.Count ?? 0);
            return false;
        }

        if (basket.Constituents is null || basket.Constituents.Count == 0)
        {
            _logger.LogWarning(
                "BasketActiveConsumer: dropping basket {BasketId} fingerprint={Fingerprint} with empty constituents",
                basket.BasketId, basket.Fingerprint);
            return false;
        }

        var symbols = basket.Constituents
            .Select(c => c?.Symbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToArray();

        if (symbols.Length == 0)
        {
            _logger.LogWarning(
                "BasketActiveConsumer: dropping basket {BasketId} fingerprint={Fingerprint} — every constituent had a blank symbol",
                basket.BasketId, basket.Fingerprint);
            return false;
        }

        _universe.SetFromBasket(
            basketId: basket.BasketId,
            fingerprint: basket.Fingerprint,
            asOfDate: basket.AsOfDate,
            symbols: symbols,
            source: basket.Source,
            updatedAtUtc: DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Basket received: basketId={BasketId} fingerprint={Fingerprint} constituents={Count} source={Source}",
            basket.BasketId, basket.Fingerprint, symbols.Length, basket.Source);
        return true;
    }
}
