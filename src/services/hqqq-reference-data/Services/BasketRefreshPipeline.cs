using Hqqq.Observability.Metrics;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Core orchestration for the active-basket lifecycle:
/// <list type="number">
///   <item>fetch a <see cref="HoldingsSnapshot"/> via the configured <see cref="IHoldingsSource"/>;</item>
///   <item>validate it through <see cref="HoldingsValidator"/>;</item>
///   <item>compute the deterministic content fingerprint;</item>
///   <item>compare with whatever <see cref="ActiveBasketStore"/> currently holds;</item>
///   <item>swap + publish when the fingerprint changes, or just re-publish on request (slow-cadence bootstrap friendliness).</item>
/// </list>
/// The pipeline never throws on transport/source issues — it returns a
/// structured <see cref="BasketRefreshResult"/> so the REST handler, the
/// background job, and the health check can all reason about the outcome.
/// </summary>
public sealed class BasketRefreshPipeline
{
    private readonly IHoldingsSource _source;
    private readonly HoldingsValidator _validator;
    private readonly ActiveBasketStore _store;
    private readonly IBasketPublisher _publisher;
    private readonly PublishHealthTracker _publishHealth;
    private readonly HqqqMetrics? _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<BasketRefreshPipeline> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BasketRefreshPipeline(
        IHoldingsSource source,
        HoldingsValidator validator,
        ActiveBasketStore store,
        IBasketPublisher publisher,
        PublishHealthTracker publishHealth,
        ILogger<BasketRefreshPipeline> logger,
        TimeProvider? clock = null,
        HqqqMetrics? metrics = null)
    {
        _source = source;
        _validator = validator;
        _store = store;
        _publisher = publisher;
        _publishHealth = publishHealth;
        _metrics = metrics;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs a single refresh: fetches the freshest snapshot, validates it,
    /// activates on fingerprint change, and publishes the active basket.
    /// Serialized under a semaphore so concurrent startup + periodic + REST
    /// refreshes don't interleave state mutations.
    /// </summary>
    public async Task<BasketRefreshResult> RefreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Re-publishes the currently-active basket without re-fetching. Used by
    /// the background job on the slow republish cadence so a late consumer
    /// (freshly-started gateway / quote-engine) hydrates without operator
    /// action. Returns a result with <c>Changed=false</c>.
    /// </summary>
    public async Task<BasketRefreshResult> RepublishCurrentAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _store.Current;
            if (current is null)
            {
                return new BasketRefreshResult
                {
                    Success = false,
                    Error = "no active basket yet",
                };
            }

            await TryPublishAsync(current, ct).ConfigureAwait(false);

            return new BasketRefreshResult
            {
                Success = true,
                Changed = false,
                Source = current.Snapshot.Source,
                Fingerprint = current.Fingerprint,
                PreviousFingerprint = current.Fingerprint,
                ConstituentCount = current.Snapshot.Constituents.Count,
                AsOfDate = current.Snapshot.AsOfDate,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BasketRefreshResult> RefreshCoreAsync(CancellationToken ct)
    {
        HoldingsFetchResult fetch;
        try
        {
            fetch = await _source.FetchAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "BasketRefreshPipeline: source {Name} threw", _source.Name);
            return new BasketRefreshResult
            {
                Success = false,
                Error = $"source threw: {ex.Message}",
            };
        }

        if (fetch.Status != HoldingsFetchStatus.Ok || fetch.Snapshot is null)
        {
            _logger.LogError(
                "BasketRefreshPipeline: source {Name} returned {Status} ({Reason}); refresh aborted",
                _source.Name, fetch.Status, fetch.Reason);
            return new BasketRefreshResult
            {
                Success = false,
                Error = $"source {_source.Name} returned {fetch.Status}: {fetch.Reason}",
            };
        }

        var snapshot = fetch.Snapshot;
        var outcome = _validator.Validate(snapshot);
        if (_validator.BlocksActivation(outcome))
        {
            _logger.LogError(
                "BasketRefreshPipeline: snapshot from {Source} failed validation ({Errors}); refresh aborted",
                snapshot.Source, string.Join("; ", outcome.Errors));
            return new BasketRefreshResult
            {
                Success = false,
                Source = snapshot.Source,
                ConstituentCount = snapshot.Constituents.Count,
                Error = $"validation failed: {string.Join("; ", outcome.Errors)}",
            };
        }

        if (!outcome.IsValid)
        {
            _logger.LogWarning(
                "BasketRefreshPipeline: snapshot from {Source} accepted under permissive validation ({Errors})",
                snapshot.Source, string.Join("; ", outcome.Errors));
        }

        var fingerprint = HoldingsFingerprint.Compute(snapshot);
        var previous = _store.Current;
        var previousFingerprint = previous?.Fingerprint;

        if (previous is not null && previousFingerprint == fingerprint)
        {
            _logger.LogInformation(
                "BasketRefreshPipeline: fingerprint unchanged ({Fingerprint}); no activation",
                fingerprint);

            return new BasketRefreshResult
            {
                Success = true,
                Changed = false,
                Source = snapshot.Source,
                Fingerprint = fingerprint,
                PreviousFingerprint = previousFingerprint,
                ConstituentCount = snapshot.Constituents.Count,
                AsOfDate = snapshot.AsOfDate,
            };
        }

        var active = new ActiveBasket
        {
            Snapshot = snapshot,
            Fingerprint = fingerprint,
            ActivatedAtUtc = _clock.GetUtcNow(),
        };

        _store.Set(active);

        _logger.LogInformation(
            "BasketRefreshPipeline: activated basketId={BasketId} version={Version} source={Source} count={Count} fingerprint={Fingerprint} previousFingerprint={PreviousFingerprint}",
            snapshot.BasketId, snapshot.Version, snapshot.Source,
            snapshot.Constituents.Count, fingerprint,
            previousFingerprint ?? "<none>");

        await TryPublishAsync(active, ct).ConfigureAwait(false);

        return new BasketRefreshResult
        {
            Success = true,
            Changed = true,
            Source = snapshot.Source,
            Fingerprint = fingerprint,
            PreviousFingerprint = previousFingerprint,
            ConstituentCount = snapshot.Constituents.Count,
            AsOfDate = snapshot.AsOfDate,
        };
    }

    private async Task TryPublishAsync(ActiveBasket active, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        _publishHealth.RecordAttempt(now);

        try
        {
            var ev = ActiveBasketEventMapper.ToEvent(active);
            await _publisher.PublishAsync(ev, ct).ConfigureAwait(false);

            _publishHealth.RecordSuccess(_clock.GetUtcNow(), active.Fingerprint);

            _logger.LogInformation(
                "Published refdata.basket.active.v1 basketId={BasketId} fingerprint={Fingerprint} constituents={Count} source={Source}",
                ev.BasketId, ev.Fingerprint, ev.Constituents.Count, ev.Source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Publish failures are not fatal for the in-memory active
            // basket — the REST layer still serves the activated snapshot.
            // But they MUST degrade readiness: we record the failure into
            // PublishHealthTracker so the health check can observe the
            // outage and downgrade the service to Degraded/Unhealthy.
            _publishHealth.RecordFailure(_clock.GetUtcNow(), ex.Message);
            _metrics?.RefdataPublishFailuresTotal.Add(1);

            _logger.LogError(ex,
                "BasketRefreshPipeline: publish failed for basketId={BasketId} fingerprint={Fingerprint}; will retry on next tick",
                active.Snapshot.BasketId, active.Fingerprint);
        }
    }
}
