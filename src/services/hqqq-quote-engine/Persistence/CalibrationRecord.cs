namespace Hqqq.QuoteEngine.Persistence;

/// <summary>
/// Persisted iNAV calibration outcome keyed by basket. Written by
/// <see cref="Services.BootstrapCalibrationCoordinator"/> the first time a
/// given basket fingerprint sees a usable QQQ tick + complete pricing
/// basis; consumed by the coordinator on restart (or replica swap) to
/// avoid re-bootstrapping against a different reference price.
/// </summary>
/// <remarks>
/// The pair <c>(anchorPrice, rawValue)</c> is retained alongside the
/// derived <c>scaleFactor</c> purely for operational traceability — the
/// engine only consumes <c>scaleFactor</c>. Records expire after a
/// configurable TTL (default 7 days) so a stale calibration cannot
/// silently survive a corporate-action event that materially changes
/// the basis.
/// </remarks>
public sealed record CalibrationRecord
{
    public required string BasketId { get; init; }
    public required string Fingerprint { get; init; }
    public required decimal ScaleFactor { get; init; }
    public required decimal AnchorPrice { get; init; }
    public required decimal RawValue { get; init; }
    public required DateTimeOffset CalibratedAtUtc { get; init; }
}
