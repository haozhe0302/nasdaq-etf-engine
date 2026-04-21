namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Ordered-arm holdings source: walks each configured primary in order
/// and returns the first snapshot that survives the validator. On every
/// primary being <c>Unavailable</c>, or failing validation (in strict
/// mode), it falls back to the deterministic seed. The lineage tag
/// (<c>Source</c>) from whichever arm wins is preserved on the returned
/// snapshot so the active basket always carries truthful provenance.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 has two kinds of primary arms registered in this order:
/// <list type="number">
///   <item><c>RealSourceBasketHoldingsSource</c> — the ported Phase 1
///   basket lifecycle (AlphaVantage + Nasdaq JSON → merged pending
///   basket). This is the production path.</item>
///   <item><c>LiveHoldingsSource</c> — configuration-driven file or
///   HTTP drop. Useful for dev/demo bring-up and as a belt-and-suspenders
///   arm when the real-source pipeline is temporarily unavailable.</item>
/// </list>
/// When <see cref="BasketMode.Seed"/> is selected, the real-source arm
/// is not registered; the composite degrades to
/// "live (file/http) → seed", matching the pre-port behaviour.
/// </para>
/// </remarks>
public sealed class CompositeHoldingsSource : IHoldingsSource
{
    private readonly IReadOnlyList<IHoldingsSource> _primaries;
    private readonly FallbackSeedHoldingsSource _fallback;
    private readonly HoldingsValidator _validator;
    private readonly ILogger<CompositeHoldingsSource> _logger;

    /// <summary>
    /// Primary ctor used in production — takes any ordered list of
    /// primary arms.
    /// </summary>
    public CompositeHoldingsSource(
        IEnumerable<IHoldingsSource> primaries,
        FallbackSeedHoldingsSource fallback,
        HoldingsValidator validator,
        ILogger<CompositeHoldingsSource> logger)
    {
        _primaries = primaries?.ToArray() ?? Array.Empty<IHoldingsSource>();
        _fallback = fallback;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>Back-compat ctor used by existing tests (single primary + fallback).</summary>
    public CompositeHoldingsSource(
        LiveHoldingsSource live,
        FallbackSeedHoldingsSource fallback,
        HoldingsValidator validator,
        ILogger<CompositeHoldingsSource> logger)
        : this(new IHoldingsSource[] { live }, fallback, validator, logger)
    {
    }

    public string Name =>
        $"composite({string.Join(",", _primaries.Select(p => p.Name))},fallback-seed)";

    public async Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        foreach (var primary in _primaries)
        {
            var result = await primary.FetchAsync(ct).ConfigureAwait(false);

            if (result.Status == HoldingsFetchStatus.Ok && result.Snapshot is not null)
            {
                var outcome = _validator.Validate(result.Snapshot);
                if (!_validator.BlocksActivation(outcome))
                {
                    _logger.LogInformation(
                        "Composite: primary {Name} accepted ({Count} constituents, asOf={AsOf}, valid={Valid})",
                        primary.Name, result.Snapshot.Constituents.Count,
                        result.Snapshot.AsOfDate, outcome.IsValid);
                    return result;
                }

                _logger.LogWarning(
                    "Composite: primary {Name} rejected by validator ({Errors}); trying next arm",
                    primary.Name, string.Join("; ", outcome.Errors));
                continue;
            }

            if (result.Status == HoldingsFetchStatus.Invalid)
            {
                _logger.LogWarning(
                    "Composite: primary {Name} returned Invalid ({Reason}); trying next arm",
                    primary.Name, result.Reason);
            }
            else
            {
                _logger.LogInformation(
                    "Composite: primary {Name} unavailable ({Reason}); trying next arm",
                    primary.Name, result.Reason ?? "no-reason");
            }
        }

        _logger.LogInformation("Composite: all primaries exhausted; using fallback seed");
        return await _fallback.FetchAsync(ct).ConfigureAwait(false);
    }
}
