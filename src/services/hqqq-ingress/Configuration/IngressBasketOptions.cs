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

    /// <summary>
    /// Symbols that are always merged into the active subscription set,
    /// regardless of basket constituents. The canonical use is the ETF
    /// anchor symbol (default <c>QQQ</c>) so quote-engine can compute
    /// the iNAV scale factor and the gateway can expose a live
    /// <c>marketPrice</c> / <c>qqq</c> value on <c>/api/quote</c>.
    /// </summary>
    /// <remarks>
    /// Merged at two seams: (1) inside
    /// <see cref="Consumers.BasketActiveConsumer"/> before every
    /// <see cref="State.ActiveSymbolUniverse.SetFromBasket"/> call so
    /// every fingerprint change keeps the anchor; (2) inside
    /// <see cref="Workers.TiingoIngressWorker"/> when falling back to
    /// the <c>Tiingo:Symbols</c> bootstrap-override path so a startup
    /// window without a basket still subscribes to the anchor.
    /// Symbols are normalized (trimmed, upper-cased, deduplicated)
    /// via <see cref="ResolveAnchorSymbols"/>.
    /// </remarks>
    public string[] AnchorSymbols { get; set; } = new[] { "QQQ" };

    /// <summary>
    /// Returns the normalized anchor symbol set: trimmed, upper-cased,
    /// blanks dropped, deduplicated by ordinal comparison. Safe to call
    /// concurrently and never returns <c>null</c>.
    /// </summary>
    public IReadOnlyList<string> ResolveAnchorSymbols()
    {
        if (AnchorSymbols is null || AnchorSymbols.Length == 0)
            return Array.Empty<string>();

        return AnchorSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
