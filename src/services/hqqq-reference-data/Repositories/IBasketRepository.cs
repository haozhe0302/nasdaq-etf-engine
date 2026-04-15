using Hqqq.Domain.Entities;

namespace Hqqq.ReferenceData.Repositories;

public interface IBasketRepository
{
    Task<BasketVersion?> GetActiveVersionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ConstituentWeight>> GetConstituentsAsync(
        string basketId,
        string versionId,
        CancellationToken ct = default);
}
