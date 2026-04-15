using Hqqq.Domain.Entities;

namespace Hqqq.ReferenceData.Services;

public interface IBasketService
{
    Task<BasketCurrentResult?> GetCurrentAsync(CancellationToken ct = default);
    Task<BasketRefreshResult> RefreshAsync(CancellationToken ct = default);
}

public sealed record BasketCurrentResult
{
    public required BasketVersion Active { get; init; }
    public required IReadOnlyList<ConstituentWeight> Constituents { get; init; }
}

public sealed record BasketRefreshResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
}
