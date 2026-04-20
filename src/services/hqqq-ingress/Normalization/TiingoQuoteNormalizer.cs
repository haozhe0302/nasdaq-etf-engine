using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Normalization;

/// <summary>
/// Pure static mappers from raw Tiingo provider fields to the canonical
/// Phase 2 wire events <see cref="RawTickV1"/> and
/// <see cref="LatestSymbolQuoteV1"/>. Centralised here so the websocket
/// client, REST snapshot client, and the Kafka publisher all agree on
/// the field projection (no per-callsite drift).
/// </summary>
public static class TiingoQuoteNormalizer
{
    public static RawTickV1 Normalize(
        string symbol,
        decimal last,
        decimal? bid,
        decimal? ask,
        string currency,
        DateTimeOffset providerTimestamp,
        long sequence)
    {
        return new RawTickV1
        {
            Symbol = symbol,
            Last = last,
            Bid = bid,
            Ask = ask,
            Currency = currency,
            Provider = "tiingo",
            ProviderTimestamp = providerTimestamp,
            IngressTimestamp = DateTimeOffset.UtcNow,
            Sequence = sequence,
        };
    }

    /// <summary>
    /// Projects a <see cref="RawTickV1"/> into the compacted
    /// <see cref="LatestSymbolQuoteV1"/> shape used on
    /// <c>market.latest_by_symbol.v1</c>. Live ticks are always
    /// <see cref="LatestSymbolQuoteV1.IsStale"/> = <c>false</c>; the
    /// stale flag is reserved for the future REST-fallback path.
    /// </summary>
    public static LatestSymbolQuoteV1 ToLatestQuote(RawTickV1 tick)
    {
        return new LatestSymbolQuoteV1
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
    }
}
