namespace Hqqq.Ingress.Configuration;

/// <summary>
/// Configuration for the <c>refdata.basket.active.v1</c> consumer that
/// drives the ingress Tiingo subscription universe. Bound from the
/// <c>Ingress:Basket</c> section.
/// </summary>
/// <remarks>
/// The default topic matches <c>Hqqq.Infrastructure.Kafka.KafkaTopics.BasketActive</c>.
/// Override only when testing against a forked topic name.
/// </remarks>
public sealed class IngressBasketOptions
{
    public const string SectionName = "Ingress:Basket";

    /// <summary>Compacted topic carrying the authoritative active basket.</summary>
    public string Topic { get; set; } = "refdata.basket.active.v1";

    /// <summary>
    /// Consumer group suffix used when building the Kafka consumer config.
    /// The shared prefix from <c>Kafka:ConsumerGroupPrefix</c> is prepended.
    /// </summary>
    public string ConsumerGroup { get; set; } = "ingress-baskets";

    /// <summary>
    /// Maximum time ingress waits for the first basket event before
    /// falling back to <c>Tiingo:Symbols</c> (if configured). When the
    /// override is also empty, the worker keeps waiting indefinitely so
    /// it doesn't silently subscribe to nothing.
    /// </summary>
    public int StartupWaitSeconds { get; set; } = 60;
}
