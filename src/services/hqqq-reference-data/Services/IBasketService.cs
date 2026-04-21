using Hqqq.Domain.Entities;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Service-facing facade for the active basket and the refresh pipeline.
/// Implemented by <see cref="BasketService"/> backed by
/// <see cref="ActiveBasketStore"/> + <see cref="BasketRefreshPipeline"/>.
/// </summary>
public interface IBasketService
{
    /// <summary>Returns the currently-active basket view, or null before first activation.</summary>
    Task<BasketCurrentResult?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Runs a real refresh and returns a structured outcome.</summary>
    Task<BasketRefreshResult> RefreshAsync(CancellationToken ct = default);
}

public sealed record BasketCurrentResult
{
    public required BasketVersion Active { get; init; }
    public required IReadOnlyList<ConstituentWeight> Constituents { get; init; }

    /// <summary>Lineage tag of the current basket (e.g. <c>"live:file"</c>, <c>"fallback-seed"</c>).</summary>
    public required string Source { get; init; }

    public required DateOnly AsOfDate { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }

    /// <summary>
    /// Kafka publish-health projection attached to the current-basket
    /// response. Lets an operator see whether the in-memory basket has
    /// actually been delivered to <c>refdata.basket.active.v1</c> without
    /// scraping <c>/healthz/ready</c>.
    /// </summary>
    public required BasketPublishStatus PublishStatus { get; init; }
}

/// <summary>
/// Publish-health projection: pulled from <see cref="PublishHealthTracker"/>
/// and the readiness thresholds so the REST layer and health endpoint
/// agree on the state name.
/// </summary>
public sealed record BasketPublishStatus
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
}

public sealed record BasketRefreshResult
{
    public required bool Success { get; init; }

    /// <summary>True if this refresh produced a different fingerprint than the prior active basket.</summary>
    public bool Changed { get; init; }

    public string? Source { get; init; }
    public string? Fingerprint { get; init; }
    public string? PreviousFingerprint { get; init; }
    public int ConstituentCount { get; init; }
    public DateOnly? AsOfDate { get; init; }
    public string? Error { get; init; }
}
