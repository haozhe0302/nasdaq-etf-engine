namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// A top constituent mover ranked by basis-point impact on the basket.
/// </summary>
public sealed record Mover
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required decimal ChangePct { get; init; }

    /// <summary>Basket impact in basis points (weight * changePct * 100).</summary>
    public required decimal Impact { get; init; }

    /// <summary>"up" or "down".</summary>
    public required string Direction { get; init; }
}
