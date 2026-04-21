using Hqqq.Ingress.State;
using Hqqq.Observability.Metrics;

namespace Hqqq.Ingress.Metrics;

/// <summary>
/// Wires observable gauges that project live ingress runtime state into
/// the Prometheus scrape exposed on <c>/metrics</c>:
/// <list type="bullet">
///   <item><c>hqqq.ingress.active_symbols</c> — current size of the
///         basket-driven Tiingo subscription set
///         (<see cref="BasketSubscriptionCoordinator.CurrentAppliedSymbols"/>.Count).</item>
///   <item><c>hqqq.ingress.basket_fingerprint_age_seconds</c> — wall-clock
///         seconds since the active basket snapshot was last updated, or
///         <c>0</c> before the first basket arrives.</item>
///   <item><c>hqqq.ingress.published_ticks_total</c> — running count of
///         ticks successfully produced to Kafka
///         (<see cref="IngestionState.PublishedTickCount"/>).</item>
///   <item><c>hqqq.ingress.last_published_tick_timestamp</c> — unix
///         seconds of the most recent successful publish, <c>0</c> if
///         nothing has been published yet.</item>
/// </list>
/// All values are read from the real ingress state singletons that drive
/// the websocket subscription and the Kafka publish path — there is no
/// parallel state. Registered as a singleton in <c>Program.cs</c> and
/// eagerly resolved so the observable callbacks are wired before the
/// first scrape.
/// </summary>
public sealed class IngressMetrics : IDisposable
{
    private readonly ActiveSymbolUniverse _universe;
    private readonly BasketSubscriptionCoordinator _coordinator;
    private readonly IngestionState _state;
    private readonly TimeProvider _clock;

    public IngressMetrics(
        ActiveSymbolUniverse universe,
        BasketSubscriptionCoordinator coordinator,
        IngestionState state,
        TimeProvider? clock = null)
    {
        _universe = universe;
        _coordinator = coordinator;
        _state = state;
        _clock = clock ?? TimeProvider.System;

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.IngressActiveSymbols,
            ObserveActiveSymbols,
            unit: "symbols");

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.IngressBasketFingerprintAgeSeconds,
            ObserveBasketFingerprintAgeSeconds,
            unit: "s");

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.IngressPublishedTicksTotal,
            ObservePublishedTicksTotal,
            unit: "ticks");

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.IngressLastPublishedTickTimestamp,
            ObserveLastPublishedTickTimestamp,
            unit: "s");
    }

    private int ObserveActiveSymbols() => _coordinator.CurrentAppliedSymbols.Count;

    private double ObserveBasketFingerprintAgeSeconds()
    {
        var current = _universe.Current;
        if (current is null) return 0;
        var age = (_clock.GetUtcNow() - current.UpdatedAtUtc).TotalSeconds;
        return Math.Max(0, age);
    }

    private long ObservePublishedTicksTotal() => _state.PublishedTickCount;

    private long ObserveLastPublishedTickTimestamp()
    {
        var ts = _state.LastPublishedTickUtc;
        return ts is null ? 0 : ts.Value.ToUnixTimeSeconds();
    }

    public void Dispose()
    {
        // Gauge callbacks stop being invoked when the static Hqqq Meter
        // is disposed. The meter lives for the life of the process, so
        // there is nothing to release here — IDisposable is implemented
        // for DI ergonomics only.
    }
}
