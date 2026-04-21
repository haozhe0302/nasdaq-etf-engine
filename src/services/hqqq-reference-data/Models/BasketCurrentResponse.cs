using Hqqq.Domain.Entities;
using Hqqq.ReferenceData.Services;

namespace Hqqq.ReferenceData.Models;

/// <summary>
/// REST response shape for <c>GET /api/basket/current</c>. Decoupled from
/// domain entities so the HTTP contract can evolve independently.
/// </summary>
public sealed record BasketCurrentResponse
{
    public required BasketVersionDto Active { get; init; }
    public required IReadOnlyList<ConstituentWeight> Constituents { get; init; }

    /// <summary>Lineage tag (e.g. <c>"live:file"</c>, <c>"fallback-seed"</c>).</summary>
    public required string Source { get; init; }

    public required DateOnly AsOfDate { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }

    /// <summary>Kafka publish-health projection — mirrors <c>/healthz/ready</c>.</summary>
    public required BasketPublishStatusDto PublishStatus { get; init; }
}

/// <summary>
/// HTTP projection of <see cref="BasketPublishStatus"/>. Kept as a
/// separate DTO so the wire shape stays independent of the internal
/// service record (e.g. property casing, future fields).
/// </summary>
public sealed record BasketPublishStatusDto
{
    public DateTimeOffset? LastPublishAttemptUtc { get; init; }
    public DateTimeOffset? LastPublishOkUtc { get; init; }
    public DateTimeOffset? LastPublishFailureUtc { get; init; }
    public int ConsecutivePublishFailures { get; init; }
    public string? LastPublishError { get; init; }
    public string? LastPublishedFingerprint { get; init; }
    public bool CurrentFingerprintPublished { get; init; }

    /// <summary><c>"healthy" | "degraded" | "unhealthy"</c>.</summary>
    public required string State { get; init; }

    public static BasketPublishStatusDto FromDomain(BasketPublishStatus s) => new()
    {
        LastPublishAttemptUtc = s.LastPublishAttemptUtc,
        LastPublishOkUtc = s.LastPublishOkUtc,
        LastPublishFailureUtc = s.LastPublishFailureUtc,
        ConsecutivePublishFailures = s.ConsecutivePublishFailures,
        LastPublishError = s.LastPublishError,
        LastPublishedFingerprint = s.LastPublishedFingerprint,
        CurrentFingerprintPublished = s.CurrentFingerprintPublished,
        State = s.State,
    };
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
