namespace Hqqq.ReferenceData.Standalone;

/// <summary>
/// Validated, in-memory projection of <see cref="BasketSeedFile"/>. The
/// fingerprint is a deterministic SHA-256 hash of the canonical
/// (constituents + scaleFactor + asOfDate) projection; identical seed
/// content across processes yields identical fingerprints, which is
/// what the engine's idempotency guard relies on.
/// </summary>
public sealed record BasketSeed
{
    public required string BasketId { get; init; }
    public required string Version { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required decimal ScaleFactor { get; init; }
    public decimal? NavPreviousClose { get; init; }
    public decimal? QqqPreviousClose { get; init; }
    public required IReadOnlyList<BasketSeedConstituent> Constituents { get; init; }

    /// <summary>Deterministic SHA-256 fingerprint (hex, lowercased).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Where the seed was loaded from (embedded resource path or override file path).</summary>
    public required string Source { get; init; }
}
