using Hqqq.Domain.ValueObjects;

namespace Hqqq.Domain.Entities;

/// <summary>
/// A versioned basket definition — maps to the future <c>basket_versions</c> table.
/// </summary>
public sealed record BasketVersion
{
    public required string BasketId { get; init; }
    public required string VersionId { get; init; }
    public required Fingerprint Fingerprint { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required BasketStatus Status { get; init; }
    public DateTimeOffset? ActivatedAtUtc { get; init; }
    public required int ConstituentCount { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
