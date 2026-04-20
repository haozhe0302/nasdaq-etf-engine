using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;
using Hqqq.ReferenceData.Standalone;

namespace Hqqq.ReferenceData.Repositories;

/// <summary>
/// Standalone-mode <see cref="IBasketRepository"/> backed by the
/// deterministic seed loaded by <see cref="BasketSeedLoader"/>. Always
/// returns the same active basket version (the seed itself) so the
/// REST surface and Kafka publisher both observe one consistent view.
/// </summary>
public sealed class SeedFileBasketRepository : IBasketRepository
{
    private readonly BasketSeed _seed;
    private readonly BasketVersion _version;
    private readonly IReadOnlyList<ConstituentWeight> _constituents;
    private readonly DateTimeOffset _activatedAtUtc;

    public SeedFileBasketRepository(BasketSeed seed)
    {
        _seed = seed;
        _activatedAtUtc = DateTimeOffset.UtcNow;

        _version = new BasketVersion
        {
            BasketId = seed.BasketId,
            VersionId = seed.Version,
            Fingerprint = new Fingerprint(seed.Fingerprint),
            AsOfDate = seed.AsOfDate,
            Status = BasketStatus.Active,
            ActivatedAtUtc = _activatedAtUtc,
            ConstituentCount = seed.Constituents.Count,
            CreatedAtUtc = _activatedAtUtc,
        };

        _constituents = seed.Constituents
            .Select(c => new ConstituentWeight
            {
                Symbol = c.Symbol,
                SecurityName = c.Name,
                Weight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = "seed",
                Sector = c.Sector,
            })
            .ToArray();
    }

    /// <summary>The activation timestamp this repo stamped at construction (used by the publisher).</summary>
    public DateTimeOffset ActivatedAtUtc => _activatedAtUtc;

    public BasketSeed Seed => _seed;

    public Task<BasketVersion?> GetActiveVersionAsync(CancellationToken ct = default)
        => Task.FromResult<BasketVersion?>(_version);

    public Task<IReadOnlyList<ConstituentWeight>> GetConstituentsAsync(
        string basketId, string versionId, CancellationToken ct = default)
        => Task.FromResult(_constituents);
}
