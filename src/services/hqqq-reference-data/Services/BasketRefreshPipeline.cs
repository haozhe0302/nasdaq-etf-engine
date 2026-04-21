using Hqqq.Observability.Metrics;
using Hqqq.ReferenceData.Basket;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Core orchestration for the active-basket lifecycle:
/// <list type="number">
///   <item>fetch a <see cref="HoldingsSnapshot"/> via the configured <see cref="IHoldingsSource"/>;</item>
///   <item>validate it through <see cref="HoldingsValidator"/>;</item>
///   <item>apply Phase-2-native corporate-action adjustments
///         (splits, renames) via
///         <see cref="CorporateActionAdjustmentService"/>;</item>
///   <item>plan basket transition (add/remove diffing + scale-factor
///         continuity) via <see cref="BasketTransitionPlanner"/>;</item>
///   <item>compute the deterministic content fingerprint on the adjusted
///         snapshot;</item>
///   <item>compare with whatever <see cref="ActiveBasketStore"/> currently holds;</item>
///   <item>swap + publish when the fingerprint changes, or just re-publish
///         on request (slow-cadence bootstrap friendliness).</item>
/// </list>
/// The pipeline never throws on transport/source/adjustment issues — it
/// returns a structured <see cref="BasketRefreshResult"/> so the REST
/// handler, the background job, and the health check can all reason about
/// the outcome.
/// </summary>
public sealed class BasketRefreshPipeline
{
    private readonly IHoldingsSource _source;
    private readonly HoldingsValidator _validator;
    private readonly CorporateActionAdjustmentService _adjustments;
    private readonly BasketTransitionPlanner _transition;
    private readonly ActiveBasketStore _store;
    private readonly IBasketPublisher _publisher;
    private readonly PublishHealthTracker _publishHealth;
    private readonly PendingBasketStore? _pending;
    private readonly HqqqMetrics? _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<BasketRefreshPipeline> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BasketRefreshPipeline(
        IHoldingsSource source,
        HoldingsValidator validator,
        CorporateActionAdjustmentService adjustments,
        BasketTransitionPlanner transition,
        ActiveBasketStore store,
        IBasketPublisher publisher,
        PublishHealthTracker publishHealth,
        ILogger<BasketRefreshPipeline> logger,
        TimeProvider? clock = null,
        HqqqMetrics? metrics = null,
        PendingBasketStore? pending = null)
    {
        _source = source;
        _validator = validator;
        _adjustments = adjustments;
        _transition = transition;
        _store = store;
        _publisher = publisher;
        _publishHealth = publishHealth;
        _pending = pending;
        _metrics = metrics;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs a single refresh: fetches the freshest snapshot, validates it,
    /// applies corporate-action adjustments + transition planning, activates
    /// on fingerprint change, and publishes the active basket. Serialized
    /// under a semaphore so concurrent startup + periodic + REST refreshes
    /// don't interleave state mutations.
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
    /// Re-publishes the currently-active basket without re-fetching. Used
    /// by the background job on the slow republish cadence so a late
    /// consumer hydrates without operator action. Returns a result with
    /// <c>Changed=false</c>.
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

            await TryPublishAsync(current, _store.Previous, _store.LatestAdjustmentReport, ct)
                .ConfigureAwait(false);

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

        // Corporate-action adjustment (splits + renames) — pure transform
        // over the snapshot + provider feed. Never throws; on error the
        // report carries the provider's message and we proceed with
        // unadjusted shares.
        AdjustedResult adjusted;
        try
        {
            adjusted = await _adjustments.AdjustAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "BasketRefreshPipeline: corp-action adjustment threw; proceeding with unadjusted snapshot");
            adjusted = new AdjustedResult(snapshot, AdjustmentReport.Empty(
                source: "unknown",
                basketAsOfDate: snapshot.AsOfDate,
                runtimeDate: DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime),
                appliedAtUtc: _clock.GetUtcNow(),
                providerError: ex.Message));
        }

        // Transition planning (add/remove diff + scale-factor continuity).
        var previous = _store.Current;
        var (adjustedSnapshot, report) = _transition.Plan(previous, adjusted.Snapshot, adjusted.Report);

        var fingerprint = HoldingsFingerprint.Compute(adjustedSnapshot);
        var previousFingerprint = previous?.Fingerprint;

        if (previous is not null && previousFingerprint == fingerprint)
        {
            _logger.LogInformation(
                "BasketRefreshPipeline: fingerprint unchanged ({Fingerprint}); no activation",
                fingerprint);

            // Keep the latest-report accurate even when we skip activation
            // (e.g. operator sees "source=file, splits=0, renames=0" on the
            // REST surface after a no-op refresh tick).
            _store.UpdateReport(report);

            return new BasketRefreshResult
            {
                Success = true,
                Changed = false,
                Source = adjustedSnapshot.Source,
                Fingerprint = fingerprint,
                PreviousFingerprint = previousFingerprint,
                ConstituentCount = adjustedSnapshot.Constituents.Count,
                AsOfDate = adjustedSnapshot.AsOfDate,
            };
        }

        var active = new ActiveBasket
        {
            Snapshot = adjustedSnapshot,
            Fingerprint = fingerprint,
            ActivatedAtUtc = _clock.GetUtcNow(),
        };

        _store.Set(active, report);

        // Phase 1 parity: once a pending basket has been promoted into
        // Active, clear the pending slot so the lifecycle scheduler's
        // "pending available" check flips to false for the next cycle.
        _pending?.Clear();

        _logger.LogInformation(
            "BasketRefreshPipeline: activated basketId={BasketId} version={Version} source={Source} count={Count} fingerprint={Fingerprint} previousFingerprint={PreviousFingerprint} splits={Splits} renames={Renames} added={Added} removed={Removed} recalibrated={Recalibrated}",
            adjustedSnapshot.BasketId, adjustedSnapshot.Version, adjustedSnapshot.Source,
            adjustedSnapshot.Constituents.Count, fingerprint,
            previousFingerprint ?? "<none>",
            report.SplitsApplied, report.RenamesApplied,
            report.AddedSymbols.Count, report.RemovedSymbols.Count,
            report.ScaleFactorRecalibrated);

        _metrics?.RefdataSplitsAppliedTotal.Add(report.SplitsApplied);
        _metrics?.RefdataRenamesAppliedTotal.Add(report.RenamesApplied);
        if (previous is not null
            && (report.AddedSymbols.Count > 0 || report.RemovedSymbols.Count > 0))
        {
            _metrics?.RefdataBasketTransitionsTotal.Add(1);
        }
        if (report.ProviderError is not null)
        {
            _metrics?.RefdataCorpActionFetchErrorsTotal.Add(1);
        }

        await TryPublishAsync(active, previous, report, ct).ConfigureAwait(false);

        return new BasketRefreshResult
        {
            Success = true,
            Changed = true,
            Source = adjustedSnapshot.Source,
            Fingerprint = fingerprint,
            PreviousFingerprint = previousFingerprint,
            ConstituentCount = adjustedSnapshot.Constituents.Count,
            AsOfDate = adjustedSnapshot.AsOfDate,
        };
    }

    private async Task TryPublishAsync(
        ActiveBasket active,
        ActiveBasket? previous,
        AdjustmentReport? report,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        _publishHealth.RecordAttempt(now);

        try
        {
            var ev = ActiveBasketEventMapper.ToEvent(active, previous, report);
            await _publisher.PublishAsync(ev, ct).ConfigureAwait(false);

            _publishHealth.RecordSuccess(_clock.GetUtcNow(), active.Fingerprint);

            _logger.LogInformation(
                "Published refdata.basket.active.v1 basketId={BasketId} fingerprint={Fingerprint} constituents={Count} source={Source} splits={Splits} renames={Renames}",
                ev.BasketId, ev.Fingerprint, ev.Constituents.Count, ev.Source,
                ev.AdjustmentSummary?.SplitsApplied ?? 0,
                ev.AdjustmentSummary?.RenamesApplied ?? 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _publishHealth.RecordFailure(_clock.GetUtcNow(), ex.Message);
            _metrics?.RefdataPublishFailuresTotal.Add(1);

            _logger.LogError(ex,
                "BasketRefreshPipeline: publish failed for basketId={BasketId} fingerprint={Fingerprint}; will retry on next tick",
                active.Snapshot.BasketId, active.Fingerprint);
        }
    }
}
