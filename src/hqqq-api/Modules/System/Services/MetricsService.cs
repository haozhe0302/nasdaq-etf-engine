using Prometheus;
using Hqqq.Api.Modules.System.Contracts;

namespace Hqqq.Api.Modules.System.Services;

/// <summary>
/// Central observability service. Holds Prometheus metric objects and rolling
/// windows for percentile computation. All methods are thread-safe.
/// </summary>
public sealed class MetricsService
{
    // ── Prometheus gauges ────────────────────────────────

    private readonly Gauge _snapshotAgeMs = Metrics.CreateGauge(
        "hqqq_snapshot_age_ms",
        "Current age of the most recent computed quote snapshot in milliseconds");

    private readonly Gauge _pricedWeightCoverage = Metrics.CreateGauge(
        "hqqq_priced_weight_coverage",
        "Ratio (0-1) of priced constituent weight in the active basket");

    private readonly Gauge _staleSymbolRatio = Metrics.CreateGauge(
        "hqqq_stale_symbol_ratio",
        "Ratio (0-1) of stale symbols to total tracked symbols");

    // ── Prometheus histograms ────────────────────────────

    private readonly Histogram _tickToQuoteMs = Metrics.CreateHistogram(
        "hqqq_tick_to_quote_ms",
        "Approximate time from tick ingestion to quote broadcast (ms)",
        new HistogramConfiguration
        {
            Buckets = [1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000]
        });

    private readonly Histogram _quoteBroadcastMs = Metrics.CreateHistogram(
        "hqqq_quote_broadcast_ms",
        "Time spent in the quote broadcast/send path per cycle (ms)",
        new HistogramConfiguration
        {
            Buckets = [0.1, 0.5, 1, 2.5, 5, 10, 25, 50, 100]
        });

    private readonly Histogram _failoverRecoverySeconds = Metrics.CreateHistogram(
        "hqqq_failover_recovery_seconds",
        "Duration from entering REST fallback to confirmed WebSocket recovery (seconds)",
        new HistogramConfiguration
        {
            Buckets = [1, 5, 10, 30, 60, 120, 300, 600]
        });

    private readonly Histogram _activationJumpBps = Metrics.CreateHistogram(
        "hqqq_activation_jump_bps",
        "Basis-point NAV discontinuity from pending-basket activation before continuity recalibration",
        new HistogramConfiguration
        {
            Buckets = [0.01, 0.1, 0.5, 1, 5, 10, 25, 50, 100, 500]
        });

    // ── Prometheus counters ──────────────────────────────

    private readonly Counter _ticksIngested = Metrics.CreateCounter(
        "hqqq_ticks_ingested_total", "Total market data ticks ingested");

    private readonly Counter _quoteBroadcasts = Metrics.CreateCounter(
        "hqqq_quote_broadcasts_total", "Total quote snapshots broadcast");

    private readonly Counter _fallbackActivations = Metrics.CreateCounter(
        "hqqq_fallback_activations_total", "Total REST fallback activations");

    private readonly Counter _basketActivations = Metrics.CreateCounter(
        "hqqq_basket_activations_total", "Total pending-basket activations");

    // ── Rolling windows for health-API percentile computation ──

    private readonly RollingWindow _tickToQuoteWindow = new(1000);
    private readonly RollingWindow _quoteBroadcastWindow = new(1000);

    // ── Latest scalar values (updated per broadcast cycle) ──

    private double _latestSnapshotAgeMs;
    private double _latestPricedWeightCoverage;
    private double _latestStaleSymbolRatio;
    private double _lastFailoverRecoverySec;
    private double _lastActivationJumpBps;
    private DateTimeOffset? _fallbackStartedAtUtc;
    private readonly object _fallbackLock = new();

    // ── Gauge setters ────────────────────────────────────

    public void SetSnapshotAge(double ms)
    {
        _snapshotAgeMs.Set(ms);
        Interlocked.Exchange(ref _latestSnapshotAgeMs, ms);
    }

    public void SetPricedWeightCoverage(double ratio)
    {
        _pricedWeightCoverage.Set(ratio);
        Interlocked.Exchange(ref _latestPricedWeightCoverage, ratio);
    }

    public void SetStaleSymbolRatio(double ratio)
    {
        _staleSymbolRatio.Set(ratio);
        Interlocked.Exchange(ref _latestStaleSymbolRatio, ratio);
    }

    // ── Histogram recorders ──────────────────────────────

    public void RecordTickToQuote(double ms)
    {
        _tickToQuoteMs.Observe(ms);
        _tickToQuoteWindow.Record(ms);
    }

    public void RecordQuoteBroadcast(double ms)
    {
        _quoteBroadcastMs.Observe(ms);
        _quoteBroadcastWindow.Record(ms);
    }

    public void RecordActivationJump(double bps)
    {
        _activationJumpBps.Observe(bps);
        Interlocked.Exchange(ref _lastActivationJumpBps, bps);
    }

    // ── Counter incrementers ─────────────────────────────

    public void IncrementTicksIngested() => _ticksIngested.Inc();
    public void IncrementQuoteBroadcasts() => _quoteBroadcasts.Inc();
    public void IncrementBasketActivations() => _basketActivations.Inc();

    // ── Fallback lifecycle tracking ──────────────────────

    public void OnFallbackActivated()
    {
        lock (_fallbackLock)
        {
            _fallbackStartedAtUtc = DateTimeOffset.UtcNow;
        }
        _fallbackActivations.Inc();
    }

    public void OnFallbackDeactivated()
    {
        lock (_fallbackLock)
        {
            if (_fallbackStartedAtUtc is not null)
            {
                var seconds = (DateTimeOffset.UtcNow - _fallbackStartedAtUtc.Value).TotalSeconds;
                _failoverRecoverySeconds.Observe(seconds);
                Interlocked.Exchange(ref _lastFailoverRecoverySec, seconds);
                _fallbackStartedAtUtc = null;
            }
        }
    }

    // ── Runtime metrics snapshot (for /api/system/health) ──

    public RuntimeMetricsSnapshot GetSnapshot()
    {
        return new RuntimeMetricsSnapshot
        {
            SnapshotAgeMs = Math.Round(Interlocked.CompareExchange(ref _latestSnapshotAgeMs, 0, 0), 2),
            PricedWeightCoverage = Math.Round(Interlocked.CompareExchange(ref _latestPricedWeightCoverage, 0, 0), 4),
            StaleSymbolRatio = Math.Round(Interlocked.CompareExchange(ref _latestStaleSymbolRatio, 0, 0), 4),
            TickToQuoteMs = _tickToQuoteWindow.GetStats(),
            QuoteBroadcastMs = _quoteBroadcastWindow.GetStats(),
            LastFailoverRecoverySeconds = _lastFailoverRecoverySec > 0
                ? Math.Round(_lastFailoverRecoverySec, 2) : null,
            LastActivationJumpBps = _lastActivationJumpBps > 0
                ? Math.Round(_lastActivationJumpBps, 2) : null,
            TotalTicksIngested = (long)_ticksIngested.Value,
            TotalQuoteBroadcasts = (long)_quoteBroadcasts.Value,
            TotalFallbackActivations = (long)_fallbackActivations.Value,
            TotalBasketActivations = (long)_basketActivations.Value,
        };
    }
}

/// <summary>
/// Fixed-capacity ring buffer of double samples with percentile computation.
/// Used to provide p50/p95/p99 latency values for the health API without
/// depending on the Prometheus query engine.
/// </summary>
internal sealed class RollingWindow
{
    private readonly double[] _samples;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public RollingWindow(int capacity)
    {
        _samples = new double[capacity];
    }

    public void Record(double value)
    {
        lock (_lock)
        {
            _samples[_head] = value;
            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }
    }

    public LatencyStats GetStats()
    {
        lock (_lock)
        {
            if (_count == 0) return LatencyStats.Empty;

            var sorted = new double[_count];
            for (int i = 0; i < _count; i++)
            {
                var idx = (_head - _count + i + _samples.Length) % _samples.Length;
                sorted[i] = _samples[idx];
            }
            Array.Sort(sorted);

            return new LatencyStats
            {
                P50 = Math.Round(Percentile(sorted, 0.50), 2),
                P95 = Math.Round(Percentile(sorted, 0.95), 2),
                P99 = Math.Round(Percentile(sorted, 0.99), 2),
                SampleCount = _count,
            };
        }
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var idx = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(idx);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var frac = idx - lower;
        return sorted[lower] * (1 - frac) + sorted[upper] * frac;
    }
}
