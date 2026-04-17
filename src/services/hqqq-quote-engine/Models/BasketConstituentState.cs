namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// Per-constituent metadata carried alongside the pricing basis. Names,
/// sectors and disclosed shares are surfaced here so the engine never
/// has to call back into the reference-data service to materialize
/// snapshots or movers.
/// </summary>
public sealed record BasketConstituentState
{
    public required string Symbol { get; init; }
    public required string SecurityName { get; init; }
    public required string Sector { get; init; }
    public decimal? TargetWeight { get; init; }
    public decimal SharesHeld { get; init; }
    public required string SharesOrigin { get; init; }
}
