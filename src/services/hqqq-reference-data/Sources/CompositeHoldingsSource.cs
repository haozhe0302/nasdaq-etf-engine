namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Two-arm holdings source: tries the configured live source first; on
/// <c>Unavailable</c> or (in strict mode) <c>Invalid</c>, falls back to the
/// deterministic seed. The lineage tag from whichever arm wins is preserved
/// on the returned <see cref="HoldingsSnapshot"/> so the active basket
/// always carries truthful provenance.
/// </summary>
public sealed class CompositeHoldingsSource : IHoldingsSource
{
    private readonly LiveHoldingsSource _live;
    private readonly FallbackSeedHoldingsSource _fallback;
    private readonly HoldingsValidator _validator;
    private readonly ILogger<CompositeHoldingsSource> _logger;

    public CompositeHoldingsSource(
        LiveHoldingsSource live,
        FallbackSeedHoldingsSource fallback,
        HoldingsValidator validator,
        ILogger<CompositeHoldingsSource> logger)
    {
        _live = live;
        _fallback = fallback;
        _validator = validator;
        _logger = logger;
    }

    public string Name => "composite(live,fallback-seed)";

    public async Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        var liveResult = await _live.FetchAsync(ct).ConfigureAwait(false);

        if (liveResult.Status == HoldingsFetchStatus.Ok && liveResult.Snapshot is not null)
        {
            var outcome = _validator.Validate(liveResult.Snapshot);
            if (!_validator.BlocksActivation(outcome))
            {
                _logger.LogInformation(
                    "Composite: live source {Name} accepted ({Count} constituents, asOf={AsOf}, valid={Valid})",
                    _live.Name, liveResult.Snapshot.Constituents.Count,
                    liveResult.Snapshot.AsOfDate, outcome.IsValid);
                return liveResult;
            }

            _logger.LogWarning(
                "Composite: live source {Name} rejected by validator ({Errors}); falling back to seed",
                _live.Name, string.Join("; ", outcome.Errors));
        }
        else if (liveResult.Status == HoldingsFetchStatus.Invalid)
        {
            _logger.LogWarning(
                "Composite: live source {Name} returned Invalid ({Reason}); falling back to seed",
                _live.Name, liveResult.Reason);
        }
        else
        {
            _logger.LogInformation(
                "Composite: live source {Name} unavailable ({Reason}); using seed",
                _live.Name, liveResult.Reason ?? "no-reason");
        }

        return await _fallback.FetchAsync(ct).ConfigureAwait(false);
    }
}
