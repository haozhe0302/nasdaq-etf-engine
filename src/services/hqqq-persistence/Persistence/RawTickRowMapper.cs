using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Pure mapper from the wire-shape <see cref="RawTickV1"/> event to the
/// database-shape <see cref="RawTickRow"/>. Centralizes UTC normalization
/// of both timestamps so the writer can stay trivial.
/// </summary>
public static class RawTickRowMapper
{
    public static RawTickRow Map(RawTickV1 tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (string.IsNullOrWhiteSpace(tick.Symbol))
            throw new ArgumentException("tick must have a non-empty Symbol", nameof(tick));
        if (string.IsNullOrWhiteSpace(tick.Currency))
            throw new ArgumentException("tick must have a non-empty Currency", nameof(tick));
        if (string.IsNullOrWhiteSpace(tick.Provider))
            throw new ArgumentException("tick must have a non-empty Provider", nameof(tick));

        return new RawTickRow
        {
            Symbol = tick.Symbol,
            ProviderTimestamp = tick.ProviderTimestamp.ToUniversalTime(),
            IngressTimestamp = tick.IngressTimestamp.ToUniversalTime(),
            Last = tick.Last,
            Bid = tick.Bid,
            Ask = tick.Ask,
            Currency = tick.Currency,
            Provider = tick.Provider,
            Sequence = tick.Sequence,
        };
    }
}
