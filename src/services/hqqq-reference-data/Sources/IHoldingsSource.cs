namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Pluggable holdings-source abstraction used by the refresh pipeline. The
/// service ships with three implementations:
/// <list type="bullet">
///   <item><see cref="LiveHoldingsSource"/> — config-driven file or HTTP drop.</item>
///   <item><see cref="FallbackSeedHoldingsSource"/> — deterministic committed seed.</item>
///   <item><see cref="CompositeHoldingsSource"/> — tries live, then falls back to the seed.</item>
/// </list>
/// Provider-specific adapters (Schwab / StockAnalysis / Nasdaq / AlphaVantage)
/// can be added later without changing any of the pipeline callers.
/// </summary>
public interface IHoldingsSource
{
    /// <summary>Short descriptive name used in logs and health data.</summary>
    string Name { get; }

    Task<HoldingsFetchResult> FetchAsync(CancellationToken ct);
}
