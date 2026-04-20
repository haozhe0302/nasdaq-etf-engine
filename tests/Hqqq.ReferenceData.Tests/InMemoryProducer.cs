using System.Collections.Concurrent;
using Confluent.Kafka;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Hand-rolled in-memory <see cref="IProducer{TKey, TValue}"/> that
/// records every produced message. Mirrors the same shape used by the
/// ingress tests so the test surface stays consistent.
/// </summary>
internal sealed class InMemoryProducer<TKey, TValue> : IProducer<TKey, TValue>
{
    public ConcurrentQueue<ProducedRecord<TKey, TValue>> Produced { get; } = new();

    /// <summary>When set, <see cref="ProduceAsync(string, Message{TKey, TValue}, CancellationToken)"/> throws.</summary>
    public Exception? FailWith { get; set; }

    public string Name => "in-memory";
    public Handle Handle => throw new NotSupportedException();

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;

        Produced.Enqueue(new ProducedRecord<TKey, TValue>(topic, message.Key, message.Value, message));
        return Task.FromResult(new DeliveryResult<TKey, TValue>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(Produced.Count - 1),
            Message = message,
        });
    }

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        TopicPartition topicPartition,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
        => ProduceAsync(topicPartition.Topic, message, cancellationToken);

    public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
        => throw new NotSupportedException();
    public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
        => throw new NotSupportedException();

    public int Poll(TimeSpan timeout) => 0;
    public int Flush(TimeSpan timeout) => 0;
    public void Flush(CancellationToken cancellationToken = default) { }
    public int AddBrokers(string brokers) => 0;
    public void SetSaslCredentials(string username, string password) { }

    public void InitTransactions(TimeSpan timeout) { }
    public void BeginTransaction() { }
    public void CommitTransaction(TimeSpan timeout) { }
    public void CommitTransaction() { }
    public void AbortTransaction(TimeSpan timeout) { }
    public void AbortTransaction() { }
    public void SendOffsetsToTransaction(
        IEnumerable<TopicPartitionOffset> offsets,
        IConsumerGroupMetadata groupMetadata,
        TimeSpan timeout)
    { }

    public void Dispose() { }
}

internal readonly record struct ProducedRecord<TKey, TValue>(
    string Topic,
    TKey Key,
    TValue Value,
    Message<TKey, TValue> Message);
