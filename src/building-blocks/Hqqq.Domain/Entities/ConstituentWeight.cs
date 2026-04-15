namespace Hqqq.Domain.Entities;

/// <summary>
/// A single constituent in a basket, reusable across reference-data and quote-engine.
/// </summary>
public sealed record ConstituentWeight
{
    public required string Symbol { get; init; }
    public required string SecurityName { get; init; }
    public decimal? Weight { get; init; }
    public required decimal SharesHeld { get; init; }
    public required string SharesOrigin { get; init; }
    public required string Sector { get; init; }
}
