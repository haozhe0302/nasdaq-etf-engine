namespace Hqqq.ReferenceData.CorporateActions.Contracts;

/// <summary>
/// Pluggable corporate-action data source used by the Phase-2-native
/// adjustment layer. The composite provider shipped in this service tries
/// the file source first (deterministic, offline-safe) and optionally
/// overlays Tiingo EOD splits when enabled; additional providers can be
/// added later behind this interface without touching the refresh
/// pipeline.
/// </summary>
public interface ICorporateActionProvider
{
    /// <summary>Short descriptive name used in logs / health data / lineage tags.</summary>
    string Name { get; }

    /// <summary>
    /// Fetches all split and rename events applicable to
    /// <paramref name="symbols"/> within the inclusive window
    /// <c>[from, to]</c>. Must never throw; errors are captured on
    /// <see cref="CorporateActionFeed.Error"/> and the caller falls back
    /// gracefully.
    /// </summary>
    Task<CorporateActionFeed> FetchAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
