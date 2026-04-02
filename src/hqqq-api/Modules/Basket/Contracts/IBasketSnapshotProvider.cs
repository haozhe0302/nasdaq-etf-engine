namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Provides the current basket composition snapshot.
/// Implementations may fetch from a remote source, a local cache, or both.
/// </summary>
public interface IBasketSnapshotProvider
{
    Task<BasketSnapshot?> GetLatestAsync(CancellationToken ct = default);
    Task RefreshAsync(CancellationToken ct = default);
}
