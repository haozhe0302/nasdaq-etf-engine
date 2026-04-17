using Hqqq.Contracts.Dtos;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.State;

/// <summary>
/// Rolling snapshot of the last computed NAV values + materialization
/// metadata. Read by both the snapshot and the delta materializer; written
/// by <c>IncrementalNavCalculator</c> under the engine's single-writer
/// invariant.
/// </summary>
public sealed class EngineRuntimeState
{
    private readonly object _sync = new();

    // ── Last computed scalars ───────────────────────────────────
    public decimal Nav { get; private set; }
    public decimal NavChangePct { get; private set; }
    public decimal MarketPrice { get; private set; }
    public decimal PremiumDiscountPct { get; private set; }
    public decimal Qqq { get; private set; }
    public decimal QqqChangePct { get; private set; }
    public decimal BasketValueB { get; private set; }
    public DateTimeOffset? LastNavCalcUtc { get; private set; }
    public DateTimeOffset? LastTickUtc { get; private set; }

    // ── Readiness ───────────────────────────────────────────────
    public QuoteReadiness Readiness { get; private set; } = QuoteReadiness.Uninitialized;
    public string? PauseReason { get; private set; }

    // ── Series ring buffer ──────────────────────────────────────
    private readonly SeriesPointDto?[] _seriesBuffer;
    private int _seriesHead;
    private int _seriesCount;

    /// <summary>
    /// The single series point recorded during the most recent compute cycle,
    /// or null if no point was recorded this cycle. Consumed by the delta
    /// materializer and then cleared (see <see cref="TakeLatestSeriesPoint"/>).
    /// </summary>
    private SeriesPointDto? _latestSeriesPoint;

    public EngineRuntimeState(int seriesCapacity = 4096)
    {
        if (seriesCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(seriesCapacity));
        _seriesBuffer = new SeriesPointDto?[seriesCapacity];
    }

    public void UpdateScalars(
        decimal nav,
        decimal navChangePct,
        decimal marketPrice,
        decimal premiumDiscountPct,
        decimal qqq,
        decimal qqqChangePct,
        decimal basketValueB,
        DateTimeOffset computedAtUtc,
        DateTimeOffset? lastTickUtc)
    {
        lock (_sync)
        {
            Nav = nav;
            NavChangePct = navChangePct;
            MarketPrice = marketPrice;
            PremiumDiscountPct = premiumDiscountPct;
            Qqq = qqq;
            QqqChangePct = qqqChangePct;
            BasketValueB = basketValueB;
            LastNavCalcUtc = computedAtUtc;
            LastTickUtc = lastTickUtc;
        }
    }

    public void SetReadiness(QuoteReadiness readiness, string? pauseReason = null)
    {
        lock (_sync)
        {
            Readiness = readiness;
            PauseReason = pauseReason;
        }
    }

    public void RecordSeriesPoint(SeriesPointDto point)
    {
        lock (_sync)
        {
            _seriesBuffer[_seriesHead] = point;
            _seriesHead = (_seriesHead + 1) % _seriesBuffer.Length;
            if (_seriesCount < _seriesBuffer.Length) _seriesCount++;
            _latestSeriesPoint = point;
        }
    }

    /// <summary>
    /// Return and consume the most recent recorded point. The snapshot
    /// materializer uses this to emit <c>LatestSeriesPoint</c> once per cycle.
    /// </summary>
    public SeriesPointDto? TakeLatestSeriesPoint()
    {
        lock (_sync)
        {
            var p = _latestSeriesPoint;
            _latestSeriesPoint = null;
            return p;
        }
    }

    public IReadOnlyList<SeriesPointDto> GetSeries()
    {
        lock (_sync)
        {
            if (_seriesCount == 0) return [];

            var result = new List<SeriesPointDto>(_seriesCount);
            for (int i = 0; i < _seriesCount; i++)
            {
                var idx = (_seriesHead - _seriesCount + i + _seriesBuffer.Length)
                    % _seriesBuffer.Length;
                if (_seriesBuffer[idx] is { } point)
                    result.Add(point);
            }
            return result;
        }
    }

    public void ClearSeries()
    {
        lock (_sync)
        {
            Array.Clear(_seriesBuffer);
            _seriesHead = 0;
            _seriesCount = 0;
            _latestSeriesPoint = null;
        }
    }
}
