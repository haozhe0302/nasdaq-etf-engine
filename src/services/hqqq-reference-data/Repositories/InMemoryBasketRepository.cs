using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;

namespace Hqqq.ReferenceData.Repositories;

/// <summary>
/// Deterministic in-memory stub. Returns the same hardcoded HQQQ basket on every call.
/// Will be replaced by a TimescaleDB-backed implementation in a later phase.
/// </summary>
public sealed class InMemoryBasketRepository : IBasketRepository
{
    private static readonly BasketVersion StubVersion = new()
    {
        BasketId = "HQQQ",
        VersionId = "v2025-stub",
        Fingerprint = new Fingerprint("stub-deterministic-fingerprint"),
        AsOfDate = new DateOnly(2025, 6, 1),
        Status = BasketStatus.Active,
        ActivatedAtUtc = new DateTimeOffset(2025, 6, 1, 14, 30, 0, TimeSpan.Zero),
        ConstituentCount = 5,
        CreatedAtUtc = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero),
    };

    private static readonly IReadOnlyList<ConstituentWeight> StubConstituents =
    [
        new() { Symbol = "AAPL",  SecurityName = "Apple Inc.",          Weight = 0.09m,  SharesHeld = 150_000m, SharesOrigin = "stub", Sector = "Technology" },
        new() { Symbol = "MSFT",  SecurityName = "Microsoft Corp.",     Weight = 0.08m,  SharesHeld = 120_000m, SharesOrigin = "stub", Sector = "Technology" },
        new() { Symbol = "NVDA",  SecurityName = "NVIDIA Corp.",        Weight = 0.07m,  SharesHeld = 100_000m, SharesOrigin = "stub", Sector = "Technology" },
        new() { Symbol = "AMZN",  SecurityName = "Amazon.com Inc.",     Weight = 0.06m,  SharesHeld = 80_000m,  SharesOrigin = "stub", Sector = "Consumer Discretionary" },
        new() { Symbol = "META",  SecurityName = "Meta Platforms Inc.",  Weight = 0.05m,  SharesHeld = 70_000m,  SharesOrigin = "stub", Sector = "Communication Services" },
    ];

    public Task<BasketVersion?> GetActiveVersionAsync(CancellationToken ct)
        => Task.FromResult<BasketVersion?>(StubVersion);

    public Task<IReadOnlyList<ConstituentWeight>> GetConstituentsAsync(
        string basketId, string versionId, CancellationToken ct)
        => Task.FromResult(StubConstituents);
}
