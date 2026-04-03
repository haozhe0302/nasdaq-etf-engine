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

    /// <summary>
    /// Deterministic hash of the normalized basket contents (sorted symbols + weights + as-of date).
    /// Used for idempotency: if a new merge produces the same fingerprint as the current
    /// active or pending basket, no new pending basket is created.
    /// </summary>
    public string Fingerprint { get; init; } = "";
}
