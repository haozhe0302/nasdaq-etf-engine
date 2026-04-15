namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Central registry of Kafka topic names used across all services.
/// All names are versioned (<c>.v1</c>) for schema evolution.
/// </summary>
public static class KafkaTopics
{
    /// <summary>
    /// Normalized market ticks from the ingress service.
    /// Key: symbol. Retention: time-based.
    /// </summary>
    public const string RawTicks = "market.raw_ticks.v1";

    /// <summary>
    /// Compacted latest quote per symbol, used by quote-engine for fast bootstrap on failover.
    /// Key: symbol. Cleanup policy: compact.
    /// </summary>
    public const string LatestBySymbol = "market.latest_by_symbol.v1";

    /// <summary>
    /// Compacted active basket version, published by reference-data on activation.
    /// Key: basketId. Cleanup policy: compact.
    /// </summary>
    public const string BasketActive = "refdata.basket.active.v1";

    /// <summary>
    /// iNAV snapshots produced by the quote-engine on every compute cycle.
    /// Key: basketId. Retention: time-based.
    /// </summary>
    public const string PricingSnapshots = "pricing.snapshots.v1";

    /// <summary>
    /// Operational incidents published by any service.
    /// Key: service name. Retention: time-based.
    /// </summary>
    public const string Incidents = "ops.incidents.v1";
}
