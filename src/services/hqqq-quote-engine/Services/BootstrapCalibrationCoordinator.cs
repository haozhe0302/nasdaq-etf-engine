using Hqqq.Domain.Services;
using Hqqq.Domain.ValueObjects;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Bootstraps the iNAV <see cref="ScaleFactor"/> so per-share NAV anchors
/// to a live <c>QQQ</c> price instead of the gross basket value
/// (~$340B) that reference-data emits as a placeholder scale of 1.
/// </summary>
/// <remarks>
/// <para>Activation sequence per basket fingerprint:</para>
/// <list type="number">
///   <item><description>
///     <see cref="OnBasketChanged"/> looks up the persisted
///     <see cref="CalibrationRecord"/> for the basket id. A matching
///     fingerprint restores the calibrated scale into
///     <see cref="BasketStateStore"/> and marks the coordinator
///     <em>Calibrated</em> — no further bootstrap is attempted.
///   </description></item>
///   <item><description>
///     A miss or stale fingerprint overrides the basket scale to
///     <see cref="ScaleFactor.Uninitialized"/> so
///     <see cref="IncrementalNavCalculator"/> reports
///     <c>QuoteReadiness.Uninitialized</c> until calibration completes.
///   </description></item>
///   <item><description>
///     <see cref="TryBootstrap"/> runs on every tick. On the anchor
///     symbol with a usable price and a fully-priced basket, the
///     coordinator computes <c>scale = anchorPrice / rawValue</c> via
///     <see cref="ScaleFactorCalibrator.Calibrate"/>, replaces the
///     basket scale, persists the record, and marks
///     <em>Calibrated</em>.
///   </description></item>
/// </list>
/// <para>
/// The coordinator is intentionally one-shot per fingerprint. A genuine
/// basket transition produces a new fingerprint which resets the
/// state machine and triggers a fresh bootstrap — the
/// <c>BasketTransitionPlanner</c>'s continuity scale would land us at
/// ≈ live QQQ anyway after this bootstrap, so the simpler "calibrate
/// every fingerprint" rule matches the production goal.
/// </para>
/// </remarks>
public sealed class BootstrapCalibrationCoordinator : IBootstrapCalibrationCoordinator
{
    private readonly BasketStateStore _baskets;
    private readonly PerSymbolQuoteStore _quotes;
    private readonly QuoteEngineOptions _options;
    private readonly ICalibrationStore _store;
    private readonly ISystemClock _clock;
    private readonly ILogger<BootstrapCalibrationCoordinator> _logger;
    private readonly object _gate = new();

    /// <summary>Fingerprint that has been (re)calibrated in this coordinator's lifetime.</summary>
    private string? _calibratedFingerprint;

    public BootstrapCalibrationCoordinator(
        BasketStateStore baskets,
        PerSymbolQuoteStore quotes,
        QuoteEngineOptions options,
        ICalibrationStore store,
        ISystemClock clock,
        ILogger<BootstrapCalibrationCoordinator> logger)
    {
        _baskets = baskets;
        _quotes = quotes;
        _options = options;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Fingerprint that has been (re)calibrated in this coordinator's
    /// lifetime, or <c>null</c> if the current basket is still waiting
    /// for its first anchor tick. Exposed for tests and operational
    /// observability (e.g. health probes); production callers should
    /// treat the basket-store scale as the authoritative signal.
    /// </summary>
    public string? CalibratedFingerprint
    {
        get { lock (_gate) return _calibratedFingerprint; }
    }

    public void OnBasketChanged()
    {
        var basket = _baskets.Current;
        if (basket is null) return;

        var record = _store.TryGet(basket.BasketId);
        if (record is not null
            && string.Equals(record.Fingerprint, basket.Fingerprint, StringComparison.Ordinal)
            && record.ScaleFactor > 0m)
        {
            // Persistent calibration matches the live basket — restore it
            // verbatim so a process restart doesn't show a Uninitialized
            // gap while we wait for the next QQQ tick.
            var restored = basket with { ScaleFactor = new ScaleFactor(record.ScaleFactor) };
            _baskets.Replace(restored);
            lock (_gate) _calibratedFingerprint = basket.Fingerprint;

            _logger.LogInformation(
                "Restored calibration for {BasketId} fp={Fingerprint}: scale={Scale} anchor={Anchor} raw={Raw} calibratedAt={At}",
                basket.BasketId, basket.Fingerprint,
                record.ScaleFactor, record.AnchorPrice, record.RawValue, record.CalibratedAtUtc);
            return;
        }

        // No matching record — fall through to fresh bootstrap. We
        // explicitly downgrade the basket scale to Uninitialized so the
        // calculator publishes Readiness=Uninitialized (instead of
        // emitting an absurd NAV ≈ gross-basket-value) during the
        // ≤ one-tick window before the first QQQ price arrives.
        if (basket.ScaleFactor.IsInitialized)
        {
            var pending = basket with { ScaleFactor = ScaleFactor.Uninitialized };
            _baskets.Replace(pending);
        }

        lock (_gate) _calibratedFingerprint = null;

        _logger.LogInformation(
            "Bootstrap pending for {BasketId} fp={Fingerprint} — waiting for {Anchor} tick to calibrate scale",
            basket.BasketId, basket.Fingerprint, _options.AnchorSymbol);
    }

    public bool TryBootstrap(string tickSymbol)
    {
        if (string.IsNullOrWhiteSpace(tickSymbol)) return false;

        // Fast path: only the anchor symbol can drive a fresh
        // calibration. Other tick paths short-circuit so we don't take
        // the lock on every tick.
        if (!string.Equals(tickSymbol, _options.AnchorSymbol, StringComparison.OrdinalIgnoreCase))
            return false;

        var basket = _baskets.Current;
        if (basket is null) return false;

        lock (_gate)
        {
            if (string.Equals(_calibratedFingerprint, basket.Fingerprint, StringComparison.Ordinal))
                return false;
        }

        // Defensive: the basket could have been replaced with a
        // calibrated scale between the read above and here.
        if (basket.ScaleFactor.IsInitialized) return false;

        var anchor = _quotes.Get(_options.AnchorSymbol);
        if (anchor is null || anchor.Price <= 0m) return false;

        var latestPrices = BuildPriceMap(basket);
        if (latestPrices.Count == 0) return false;

        var rawValue = BasketRawValueCalculator.Compute(basket.PricingBasis.Entries, latestPrices);
        if (rawValue <= 0m) return false;

        var scale = ScaleFactorCalibrator.Calibrate(anchor.Price, rawValue);
        if (!scale.IsInitialized) return false;

        var calibrated = basket with { ScaleFactor = scale };
        _baskets.Replace(calibrated);

        var record = new CalibrationRecord
        {
            BasketId = basket.BasketId,
            Fingerprint = basket.Fingerprint,
            ScaleFactor = scale.Value,
            AnchorPrice = anchor.Price,
            RawValue = rawValue,
            CalibratedAtUtc = _clock.UtcNow,
        };
        _store.Set(record);

        lock (_gate) _calibratedFingerprint = basket.Fingerprint;

        _logger.LogInformation(
            "Calibrated {BasketId} fp={Fingerprint}: scale={Scale} anchor={Anchor} raw={Raw} → nav≈{Nav}",
            basket.BasketId, basket.Fingerprint,
            scale.Value, anchor.Price, rawValue, scale.Value * rawValue);

        return true;
    }

    private IReadOnlyDictionary<string, decimal> BuildPriceMap(Models.ActiveBasket basket)
    {
        var map = new Dictionary<string, decimal>(
            capacity: basket.PricingBasis.Entries.Count,
            comparer: StringComparer.OrdinalIgnoreCase);
        foreach (var entry in basket.PricingBasis.Entries)
        {
            var state = _quotes.Get(entry.Symbol);
            if (state is not null && state.Price > 0m)
                map[entry.Symbol] = state.Price;
        }
        return map;
    }
}
