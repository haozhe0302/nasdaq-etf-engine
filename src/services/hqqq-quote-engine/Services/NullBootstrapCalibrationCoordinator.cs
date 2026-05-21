namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// No-op <see cref="IBootstrapCalibrationCoordinator"/> for tests and
/// runtime paths that don't want the engine to override the basket
/// scale factor. Existing unit fixtures construct baskets with explicit
/// scale values (e.g. <c>0.001m</c>) and rely on the calculator
/// running against that scale — injecting this coordinator preserves
/// that legacy behaviour without touching the test rig wiring.
/// </summary>
public sealed class NullBootstrapCalibrationCoordinator : IBootstrapCalibrationCoordinator
{
    /// <summary>Process-wide singleton; the class is stateless.</summary>
    public static readonly NullBootstrapCalibrationCoordinator Instance = new();

    public void OnBasketChanged() { }

    public bool TryBootstrap(string tickSymbol) => false;
}
