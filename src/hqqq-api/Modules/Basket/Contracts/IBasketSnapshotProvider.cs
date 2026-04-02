namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Provides the current basket composition with active/pending semantics.
/// </summary>
public interface IBasketSnapshotProvider
{
    /// <summary>Returns the basket currently used for pricing (the active basket).</summary>
    Task<BasketSnapshot?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Returns the full active + pending basket state for inspection.</summary>
    BasketState GetState();

    /// <summary>
    /// Fetches fresh holdings and stores the result as either active or pending,
    /// depending on whether the market is currently open.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Promotes the pending basket to active. Called at market open.
    /// </summary>
    void ActivatePendingIfReady();
}
