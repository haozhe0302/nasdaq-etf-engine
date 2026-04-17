using System.Collections.Concurrent;
using Hqqq.Domain.Services;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.State;

/// <summary>
/// Thread-safe in-memory latest-quote store keyed by symbol. Single logical
/// owner per engine instance; concurrent writers are serialized via the
/// underlying <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Ported shape from <c>Hqqq.Api.Modules.MarketData.Services.InMemoryLatestPriceStore</c>
/// but without legacy roles / health snapshot coupling.
/// </summary>
public sealed class PerSymbolQuoteStore
{
    private readonly ConcurrentDictionary<string, PerSymbolQuoteState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ISystemClock _clock;

    public PerSymbolQuoteStore(ISystemClock clock)
    {
        _clock = clock;
    }

    public int Count => _states.Count;

    public void Update(NormalizedTick tick)
    {
        var receivedAt = _clock.UtcNow;

        _states.AddOrUpdate(
            tick.Symbol,
            _ => new PerSymbolQuoteState
            {
                Symbol = tick.Symbol,
                Price = tick.Last,
                ReceivedAtUtc = receivedAt,
                Provider = tick.Provider,
                Sequence = tick.Sequence,
                PreviousClose = tick.PreviousClose,
                Bid = tick.Bid,
                Ask = tick.Ask,
            },
            (_, existing) => new PerSymbolQuoteState
            {
                Symbol = tick.Symbol,
                Price = tick.Last,
                ReceivedAtUtc = receivedAt,
                Provider = tick.Provider,
                Sequence = tick.Sequence,
                PreviousClose = tick.PreviousClose ?? existing.PreviousClose,
                Bid = tick.Bid ?? existing.Bid,
                Ask = tick.Ask ?? existing.Ask,
            });
    }

    public PerSymbolQuoteState? Get(string symbol) =>
        _states.TryGetValue(symbol, out var s) ? s : null;

    public IReadOnlyDictionary<string, PerSymbolQuoteState> Snapshot() =>
        _states.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Build a freshness summary restricted to the passed tracked symbol list
    /// (i.e. today's active basket). Symbols that are tracked but have never
    /// received a tick count as stale.
    /// </summary>
    public FreshnessSummary BuildFreshnessSummary(
        IReadOnlyCollection<string> trackedSymbols,
        TimeSpan staleAfter)
    {
        var now = _clock.UtcNow;
        var observations = new Dictionary<string, DateTimeOffset>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var sym in trackedSymbols)
        {
            if (_states.TryGetValue(sym, out var s))
                observations[sym] = s.ReceivedAtUtc;
        }

        return FreshnessSummarizer.Summarize(trackedSymbols, observations, now, staleAfter);
    }

    /// <summary>Test / ops hook — drop everything.</summary>
    public void Clear() => _states.Clear();
}
