using Hqqq.Api.Modules.Basket.Contracts;

namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// The output of the corporate-action adjustment layer:
/// a new <see cref="BasketSnapshot"/> clone with adjusted shares
/// plus a full audit <see cref="AdjustmentReport"/>.
/// The original snapshot is never mutated.
/// </summary>
public sealed record AdjustedBasketResult
{
    /// <summary>
    /// Basket snapshot with adjusted <c>SharesHeld</c> values.
    /// Retains the original <c>Fingerprint</c> so that pricing-state
    /// persistence and active/pending semantics are unaffected.
    /// </summary>
    public required BasketSnapshot AdjustedSnapshot { get; init; }

    public required AdjustmentReport Report { get; init; }
}
