using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Normalization;

/// <summary>
/// Pure static mapper from raw Tiingo provider fields to the canonical <see cref="RawTickV1"/> event.
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
}
