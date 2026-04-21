using Confluent.Kafka;
using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Publishing;

/// <summary>
/// Production <see cref="ITickPublisher"/> backed by two long-lived
/// Confluent Kafka producers (one per topic, both keyed by symbol).
/// Ingress owns market-tick publishing end-to-end in Phase 2 — there is
/// no stub / log-only path.
/// </summary>
/// <remarks>
/// <para>
/// Per call we produce twice:
/// <list type="bullet">
///   <item><see cref="RawTickV1"/> → <see cref="KafkaTopics.RawTicks"/></item>
///   <item><see cref="LatestSymbolQuoteV1"/> → <see cref="KafkaTopics.LatestBySymbol"/> (compacted)</item>
/// </list>
/// Both produces are awaited together so a transient broker error in
/// either path bubbles up to the caller (which records it in
/// <see cref="State.IngestionState"/>).
/// </para>
/// <para>
/// The constructor accepts pre-built producers so unit tests can inject
/// in-memory fakes without touching <see cref="KafkaConfigBuilder"/>.
/// </para>
/// </remarks>
public sealed class KafkaTickPublisher : ITickPublisher, IDisposable
{
    private readonly IProducer<string, RawTickV1> _rawProducer;
    private readonly IProducer<string, LatestSymbolQuoteV1> _latestProducer;
    private readonly bool _ownsProducers;
    private bool _disposed;

    public KafkaTickPublisher(IOptions<KafkaOptions> kafkaOptions)
    {
        var config = KafkaConfigBuilder.BuildProducerConfig(kafkaOptions.Value);
        _rawProducer = new ProducerBuilder<string, RawTickV1>(config)
            .SetValueSerializer(new JsonValueSerializer<RawTickV1>())
            .Build();
        _latestProducer = new ProducerBuilder<string, LatestSymbolQuoteV1>(config)
            .SetValueSerializer(new JsonValueSerializer<LatestSymbolQuoteV1>())
            .Build();
        _ownsProducers = true;
    }

    /// <summary>
    /// Test seam: lets unit tests provide in-memory producers without a
    /// real broker. Producers are not disposed by this constructor.
    /// </summary>
    public KafkaTickPublisher(
        IProducer<string, RawTickV1> rawProducer,
        IProducer<string, LatestSymbolQuoteV1> latestProducer)
    {
        _rawProducer = rawProducer;
        _latestProducer = latestProducer;
        _ownsProducers = false;
    }

    public async Task PublishAsync(RawTickV1 tick, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var rawMessage = new Message<string, RawTickV1>
        {
            Key = tick.Symbol,
            Value = tick,
            Timestamp = new Timestamp(tick.IngressTimestamp.UtcDateTime, TimestampType.CreateTime),
        };

        var latest = new LatestSymbolQuoteV1
        {
            Symbol = tick.Symbol,
            Last = tick.Last,
            Bid = tick.Bid,
            Ask = tick.Ask,
            Currency = tick.Currency,
            Provider = tick.Provider,
            ProviderTimestamp = tick.ProviderTimestamp,
            IngressTimestamp = tick.IngressTimestamp,
            IsStale = false,
        };

        var latestMessage = new Message<string, LatestSymbolQuoteV1>
        {
            Key = tick.Symbol,
            Value = latest,
            Timestamp = new Timestamp(tick.IngressTimestamp.UtcDateTime, TimestampType.CreateTime),
        };

        var rawTask = _rawProducer.ProduceAsync(KafkaTopics.RawTicks, rawMessage, ct);
        var latestTask = _latestProducer.ProduceAsync(KafkaTopics.LatestBySymbol, latestMessage, ct);

        await Task.WhenAll(rawTask, latestTask).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct)
    {
        foreach (var tick in ticks)
        {
            await PublishAsync(tick, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_ownsProducers) return;

        try { _rawProducer.Flush(TimeSpan.FromSeconds(5)); } catch (KafkaException) { }
        try { _latestProducer.Flush(TimeSpan.FromSeconds(5)); } catch (KafkaException) { }

        _rawProducer.Dispose();
        _latestProducer.Dispose();
    }
}
