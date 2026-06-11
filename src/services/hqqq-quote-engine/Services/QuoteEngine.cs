using Hqqq.Contracts.Dtos;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Default <see cref="IQuoteEngine"/> implementation. Single logical owner
/// per basket family: all state mutations funnel through this class.
/// </summary>
public sealed class QuoteEngine : IQuoteEngine
{
    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly EngineRuntimeState _runtime;
    private readonly IncrementalNavCalculator _calculator;
    private readonly SnapshotMaterializer _snapshotMaterializer;
    private readonly QuoteDeltaMaterializer _deltaMaterializer;
    private readonly IBootstrapCalibrationCoordinator _calibrationCoordinator;

    public QuoteEngine(
        PerSymbolQuoteStore quotes,
        BasketStateStore baskets,
        EngineRuntimeState runtime,
        IncrementalNavCalculator calculator,
        SnapshotMaterializer snapshotMaterializer,
        QuoteDeltaMaterializer deltaMaterializer,
        IBootstrapCalibrationCoordinator calibrationCoordinator)
    {
        _quotes = quotes;
        _baskets = baskets;
        _runtime = runtime;
        _calculator = calculator;
        _snapshotMaterializer = snapshotMaterializer;
        _deltaMaterializer = deltaMaterializer;
        _calibrationCoordinator = calibrationCoordinator;
    }

    public bool IsInitialized =>
        _baskets.Current is { ScaleFactor.IsInitialized: true };

    public void OnTick(NormalizedTick tick)
    {
        _quotes.Update(tick);
        // Bootstrap calibration runs before the materializer so the
        // first compute cycle that follows a freshly-arrived QQQ tick
        // already sees a properly scaled basket and emits a real NAV
        // instead of one transient Uninitialized snapshot.
        _calibrationCoordinator.TryBootstrap(tick.Symbol);
        _calculator.TryRecompute();
    }

    public void OnBasketActivated(ActiveBasket basket)
    {
        _baskets.Replace(basket);
        // New basis implies a new series context — clear session-retained
        // points so future B3 wiring can't leak across basket versions. The legacy
        // monolith does the same on a new-trading-day boundary.
        _runtime.ClearSeries();
        // Coordinator either restores a persisted calibration or downgrades
        // the basket to ScaleFactor.Uninitialized so the calculator stays
        // off-air until the first anchor tick lands.
        _calibrationCoordinator.OnBasketChanged();
        _calculator.TryRecompute();
    }

    public QuoteSnapshotDto? BuildSnapshot() => _snapshotMaterializer.Build();
    public QuoteUpdateDto? BuildDelta() => _deltaMaterializer.Build();
}
