namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// <see cref="IHoldingsSource"/> backed by the committed deterministic
/// basket-seed JSON. Always returns the same snapshot for the process
/// lifetime (seed is read once at construction). Used both as the outright
/// source when live is disabled and as the fallback arm of
/// <see cref="CompositeHoldingsSource"/>.
/// </summary>
public sealed class FallbackSeedHoldingsSource : IHoldingsSource
{
    private readonly HoldingsSnapshot _seed;
    private readonly ILogger<FallbackSeedHoldingsSource> _logger;

    public FallbackSeedHoldingsSource(
        BasketSeedLoader loader,
        ILogger<FallbackSeedHoldingsSource> logger)
    {
        _seed = loader.Load();
        _logger = logger;
    }

    public string Name => "fallback-seed";

    public Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        _logger.LogDebug(
            "FallbackSeedHoldingsSource: returning seed basketId={BasketId} version={Version} count={Count}",
            _seed.BasketId, _seed.Version, _seed.Constituents.Count);
        return Task.FromResult(HoldingsFetchResult.Ok(_seed));
    }
}
