using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.System.Services;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Thread-safe in-memory latest-price store backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Roles are swapped atomically via volatile reference replacement.
/// </summary>
public sealed class InMemoryLatestPriceStore : ILatestPriceStore
{
    private readonly ConcurrentDictionary<string, PriceEntry> _prices =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyDictionary<string, SymbolRole> _roles =
        new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase);

    private readonly int _staleAfterSeconds;
    private readonly MetricsService _metrics;

    private readonly ConcurrentQueue<long> _tickTimestamps = new();
    private long _lastActivityTicks;
    private const int MaxTickSamples = 500;

    public InMemoryLatestPriceStore(IOptions<TiingoOptions> options, MetricsService metrics)
    {
        _staleAfterSeconds = options.Value.StaleAfterSeconds;
        _metrics = metrics;
    }

    // ── Writes ──────────────────────────────────────────────────

    public void Update(PriceTick tick)
    {
        var now = DateTimeOffset.UtcNow;

        _prices.AddOrUpdate(
            tick.Symbol,
            _ => CreateEntry(tick, now, null),
            (_, existing) => CreateEntry(tick, now, existing));

        Interlocked.Exchange(ref _lastActivityTicks, now.UtcTicks);
        _tickTimestamps.Enqueue(now.UtcTicks);
        while (_tickTimestamps.Count > MaxTickSamples)
            _tickTimestamps.TryDequeue(out _);

        _metrics.IncrementTicksIngested();
    }

    public void SetTrackedSymbols(IReadOnlyDictionary<string, SymbolRole> symbolRoles)
    {
        _roles = symbolRoles.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var key in _prices.Keys)
        {
            if (!symbolRoles.ContainsKey(key))
                _prices.TryRemove(key, out _);
        }
    }

    // ── Reads ───────────────────────────────────────────────────

    public LatestPriceState? Get(string symbol) =>
        _prices.TryGetValue(symbol, out var entry) ? ToState(entry) : null;

    public IReadOnlyDictionary<string, LatestPriceState> GetAll() =>
        _prices.ToDictionary(
            kvp => kvp.Key,
            kvp => ToState(kvp.Value),
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LatestPriceState> GetLatest(IEnumerable<string> symbols)
    {
        var result = new List<LatestPriceState>();
        foreach (var symbol in symbols)
        {
            if (_prices.TryGetValue(symbol, out var entry))
                result.Add(ToState(entry));
        }
        return result;
    }

    public IReadOnlyDictionary<string, SymbolRole> GetTrackedSymbols() => _roles;

    // ── Health snapshot ─────────────────────────────────────────

    public FeedHealthSnapshot GetHealthSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var staleThreshold = now.AddSeconds(-_staleAfterSeconds);
        var roles = _roles;

        int activeCount = 0, pendingCount = 0;
        int activeWithPrice = 0, pendingWithPrice = 0;
        int staleActive = 0, stalePending = 0;
        int symbolsWithPrice = 0, totalStale = 0;

        foreach (var (symbol, role) in roles)
        {
            bool isActive = role.HasFlag(SymbolRole.Active);
            bool isPending = role.HasFlag(SymbolRole.Pending);
            bool hasPrice = _prices.TryGetValue(symbol, out var entry);
            bool stale = !hasPrice || entry!.ReceivedAtUtc < staleThreshold;

            if (isActive) activeCount++;
            if (isPending) pendingCount++;
            if (hasPrice)
            {
                symbolsWithPrice++;
                if (isActive) activeWithPrice++;
                if (isPending) pendingWithPrice++;
            }
            if (stale) totalStale++;
            if (isActive && stale) staleActive++;
            if (isPending && stale) stalePending++;
        }

        var lastTicks = Interlocked.Read(ref _lastActivityTicks);
        DateTimeOffset? lastActivity = lastTicks > 0
            ? new DateTimeOffset(lastTicks, TimeSpan.Zero) : null;

        decimal activeCoverage = activeCount > 0
            ? Math.Round((decimal)activeWithPrice / activeCount * 100m, 2) : 0m;
        decimal pendingCoverage = pendingCount > 0
            ? Math.Round((decimal)pendingWithPrice / pendingCount * 100m, 2) : 0m;

        bool pendingReady = pendingCount == 0
            || (pendingCoverage >= 95m
                && (decimal)stalePending / pendingCount <= 0.05m);

        return new FeedHealthSnapshot
        {
            WebSocketConnected = false,
            FallbackActive = false,
            SymbolsTracked = roles.Count,
            SymbolsWithPrice = symbolsWithPrice,
            StaleSymbolCount = totalStale,
            AsOfUtc = now,
            ActiveSymbolCount = activeCount,
            PendingSymbolCount = pendingCount,
            ActiveWithPriceCount = activeWithPrice,
            PendingWithPriceCount = pendingWithPrice,
            StaleActiveCount = staleActive,
            StalePendingCount = stalePending,
            ActiveCoveragePct = activeCoverage,
            PendingCoveragePct = pendingCoverage,
            LastUpstreamActivityUtc = lastActivity,
            AverageTickIntervalMs = ComputeAverageTickInterval(),
            IsPendingBasketReady = pendingReady,
        };
    }

    // ── Internals ───────────────────────────────────────────────

    private LatestPriceState ToState(PriceEntry entry)
    {
        bool isStale = (DateTimeOffset.UtcNow - entry.ReceivedAtUtc).TotalSeconds > _staleAfterSeconds;
        _roles.TryGetValue(entry.Symbol, out var role);

        return new LatestPriceState
        {
            Symbol = entry.Symbol,
            Price = entry.Price,
            ReceivedAtUtc = entry.ReceivedAtUtc,
            Source = entry.Source,
            IsStale = isStale,
            PreviousClose = entry.PreviousClose,
            BidPrice = entry.BidPrice,
            AskPrice = entry.AskPrice,
            LastTradeTimestampUtc = entry.LastTradeTimestampUtc,
            Role = role,
        };
    }

    private double? ComputeAverageTickInterval()
    {
        var timestamps = _tickTimestamps.ToArray();
        if (timestamps.Length < 2) return null;

        Array.Sort(timestamps);
        double totalMs = 0;
        for (int i = 1; i < timestamps.Length; i++)
            totalMs += TimeSpan.FromTicks(timestamps[i] - timestamps[i - 1]).TotalMilliseconds;

        return Math.Round(totalMs / (timestamps.Length - 1), 2);
    }

    private static PriceEntry CreateEntry(PriceTick tick, DateTimeOffset receivedAt, PriceEntry? existing)
    {
        return new PriceEntry
        {
            Symbol = tick.Symbol,
            Price = tick.Price,
            ReceivedAtUtc = receivedAt,
            Source = tick.Source,
            BidPrice = tick.BidPrice,
            AskPrice = tick.AskPrice,
            PreviousClose = tick.PreviousClose ?? existing?.PreviousClose,
            LastTradeTimestampUtc = tick.LastTradeTimestampUtc ?? existing?.LastTradeTimestampUtc,
        };
    }

    private sealed record PriceEntry
    {
        public required string Symbol { get; init; }
        public required decimal Price { get; init; }
        public required DateTimeOffset ReceivedAtUtc { get; init; }
        public required string Source { get; init; }
        public decimal? BidPrice { get; init; }
        public decimal? AskPrice { get; init; }
        public decimal? PreviousClose { get; init; }
        public DateTimeOffset? LastTradeTimestampUtc { get; init; }
    }
}
