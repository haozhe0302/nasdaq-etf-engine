using Hqqq.Domain.Entities;

namespace Hqqq.ReferenceData.Models;

/// <summary>
/// REST response shape for GET /api/basket/current.
/// Decoupled from the domain entities so the HTTP contract can evolve independently.
/// </summary>
public sealed record BasketCurrentResponse
{
    public required BasketVersionDto Active { get; init; }
    public required IReadOnlyList<ConstituentWeight> Constituents { get; init; }
}

public sealed record BasketVersionDto
{
    public required string BasketId { get; init; }
    public required string VersionId { get; init; }
    public required string Fingerprint { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ActivatedAtUtc { get; init; }
    public required int ConstituentCount { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public static BasketVersionDto FromDomain(BasketVersion v) => new()
    {
        BasketId = v.BasketId,
        VersionId = v.VersionId,
        Fingerprint = v.Fingerprint,
        AsOfDate = v.AsOfDate,
        Status = v.Status.ToString(),
        ActivatedAtUtc = v.ActivatedAtUtc,
        ConstituentCount = v.ConstituentCount,
        CreatedAtUtc = v.CreatedAtUtc,
    };
}
