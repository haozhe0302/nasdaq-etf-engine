using Hqqq.Api.Modules.Basket.Contracts;

namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// Applies corporate-action adjustments (currently: stock splits) to a basket
/// snapshot, producing an adjusted clone with full provenance.
/// Sits between basket retrieval and pricing-basis construction.
/// </summary>
public interface ICorporateActionAdjustmentService
{
    /// <summary>
    /// Returns an adjusted copy of <paramref name="snapshot"/> where share counts
    /// reflect any splits that occurred between the basket's <c>AsOfDate</c> and
    /// the current runtime date.  The original snapshot is never mutated.
    /// </summary>
    Task<AdjustedBasketResult> AdjustAsync(
        BasketSnapshot snapshot,
        CancellationToken ct = default);

    /// <summary>
    /// The most recent adjustment report, for diagnostics.
    /// Null until the first <see cref="AdjustAsync"/> call completes.
    /// </summary>
    AdjustmentReport? LastReport { get; }
}
