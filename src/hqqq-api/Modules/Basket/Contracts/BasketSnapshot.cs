namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// A point-in-time snapshot of the full ETF basket composition.
/// </summary>
public sealed record BasketSnapshot
{
    public required DateOnly AsOfDate { get; init; }
    public required IReadOnlyList<BasketConstituent> Constituents { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required BasketSourceInfo Source { get; init; }
    public BasketQualityReport? QualityReport { get; init; }
}
