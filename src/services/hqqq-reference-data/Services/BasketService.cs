using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Thin facade that projects <see cref="ActiveBasketStore"/> +
/// <see cref="PublishHealthTracker"/> into the service-facing DTOs
/// consumed by <see cref="Endpoints.BasketEndpoints"/>, and routes
/// refresh requests to <see cref="BasketRefreshPipeline"/>. The publish
/// status surfaced here mirrors the state driving
/// <see cref="Health.ActiveBasketHealthCheck"/> — both read the same
/// tracker and run through the same
/// <see cref="PublishHealthStateEvaluator"/>.
/// </summary>
public sealed class BasketService : IBasketService
{
    private readonly ActiveBasketStore _store;
    private readonly BasketRefreshPipeline _pipeline;
    private readonly PublishHealthTracker _publishHealth;
    private readonly PublishHealthOptions _publishHealthOptions;
    private readonly TimeProvider _clock;

    public BasketService(
        ActiveBasketStore store,
        BasketRefreshPipeline pipeline,
        PublishHealthTracker publishHealth,
        IOptions<ReferenceDataOptions>? options = null,
        TimeProvider? clock = null)
    {
        _store = store;
        _pipeline = pipeline;
        _publishHealth = publishHealth;
        _publishHealthOptions = options?.Value.PublishHealth ?? new PublishHealthOptions();
        _clock = clock ?? TimeProvider.System;
    }

    public Task<BasketCurrentResult?> GetCurrentAsync(CancellationToken ct = default)
    {
        var current = _store.Current;
        if (current is null)
            return Task.FromResult<BasketCurrentResult?>(null);

        var snapshot = current.Snapshot;
        var sharesOrigin = snapshot.Source;

        var version = new BasketVersion
        {
            BasketId = snapshot.BasketId,
            VersionId = snapshot.Version,
            Fingerprint = new Fingerprint(current.Fingerprint),
            AsOfDate = snapshot.AsOfDate,
            Status = BasketStatus.Active,
            ActivatedAtUtc = current.ActivatedAtUtc,
            ConstituentCount = snapshot.Constituents.Count,
            CreatedAtUtc = current.ActivatedAtUtc,
        };

        var constituents = snapshot.Constituents
            .Select(c => new ConstituentWeight
            {
                Symbol = c.Symbol,
                SecurityName = c.Name,
                Weight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = sharesOrigin,
                Sector = c.Sector,
            })
            .ToArray();

        var publishSnapshot = _publishHealth.Snapshot;
        var now = _clock.GetUtcNow();
        var state = PublishHealthStateEvaluator.Evaluate(
            current, publishSnapshot, _publishHealthOptions, now);

        var publishStatus = new BasketPublishStatus
        {
            LastPublishAttemptUtc = publishSnapshot.LastPublishAttemptUtc,
            LastPublishOkUtc = publishSnapshot.LastPublishOkUtc,
            LastPublishFailureUtc = publishSnapshot.LastPublishFailureUtc,
            ConsecutivePublishFailures = publishSnapshot.ConsecutivePublishFailures,
            LastPublishError = publishSnapshot.LastPublishError,
            LastPublishedFingerprint = publishSnapshot.LastPublishedFingerprint,
            CurrentFingerprintPublished =
                publishSnapshot.LastPublishedFingerprint is not null
                && publishSnapshot.LastPublishedFingerprint == current.Fingerprint,
            State = PublishHealthStateEvaluator.ToLowerString(state),
        };

        return Task.FromResult<BasketCurrentResult?>(new BasketCurrentResult
        {
            Active = version,
            Constituents = constituents,
            Source = snapshot.Source,
            AsOfDate = snapshot.AsOfDate,
            ActivatedAtUtc = current.ActivatedAtUtc,
            PublishStatus = publishStatus,
        });
    }

    public Task<BasketRefreshResult> RefreshAsync(CancellationToken ct = default)
        => _pipeline.RefreshAsync(ct);
}
