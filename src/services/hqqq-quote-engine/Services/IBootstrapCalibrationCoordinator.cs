namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Engine seam invoked by <see cref="QuoteEngine"/> at basket-activation
/// and tick-handling boundaries so the iNAV scale factor can be
/// bootstrapped from a live QQQ price. The interface keeps the engine
/// agnostic of persistence concerns (Redis in production, in-memory in
/// tests) and lets unit-level test rigs short-circuit calibration
/// entirely via <see cref="NullBootstrapCalibrationCoordinator"/>.
/// </summary>
public interface IBootstrapCalibrationCoordinator
{
    /// <summary>
    /// Invoked from <see cref="QuoteEngine.OnBasketActivated"/> after
    /// <c>BasketStateStore.Replace</c> but before
    /// <c>IncrementalNavCalculator.TryRecompute</c>. Implementations are
    /// expected to either restore a previously calibrated scale (from
    /// persistence) or transition the basket to
    /// <c>ScaleFactor.Uninitialized</c> so subsequent ticks can drive
    /// a fresh bootstrap.
    /// </summary>
    void OnBasketChanged();

    /// <summary>
    /// Invoked from <see cref="QuoteEngine.OnTick"/> after the
    /// per-symbol quote store has been updated but before the
    /// materializer runs. Implementations should fast-exit on tick
    /// symbols that aren't the anchor (default <c>QQQ</c>) and on
    /// baskets that are already calibrated. Returns true when the call
    /// actually performed a calibration step (used by tests to assert
    /// the bootstrap fired exactly once).
    /// </summary>
    bool TryBootstrap(string tickSymbol);
}
