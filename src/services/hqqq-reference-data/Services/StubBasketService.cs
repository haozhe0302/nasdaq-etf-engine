using Hqqq.ReferenceData.Repositories;

namespace Hqqq.ReferenceData.Services;

public sealed class StubBasketService(IBasketRepository repository) : IBasketService
{
    public async Task<BasketCurrentResult?> GetCurrentAsync(CancellationToken ct)
    {
        var version = await repository.GetActiveVersionAsync(ct);
        if (version is null)
            return null;

        var constituents = await repository.GetConstituentsAsync(
            version.BasketId, version.VersionId, ct);

        return new BasketCurrentResult
        {
            Active = version,
            Constituents = constituents,
        };
    }

    public Task<BasketRefreshResult> RefreshAsync(CancellationToken ct)
    {
        return Task.FromResult(new BasketRefreshResult
        {
            Success = false,
            Error = "Basket refresh not yet implemented (Phase 2B)",
        });
    }
}
