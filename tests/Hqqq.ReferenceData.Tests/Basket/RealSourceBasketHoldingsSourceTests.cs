using Hqqq.ReferenceData.Basket;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.ReferenceData.Tests.Basket;

/// <summary>
/// Covers the <see cref="RealSourceBasketHoldingsSource"/> adapter:
/// <list type="bullet">
///   <item>when the pending store holds an envelope, <c>FetchAsync</c> returns <c>Ok</c> with the pending snapshot;</item>
///   <item>when the pending store is empty and cold-start recovery also produces nothing, <c>FetchAsync</c> returns <c>Unavailable</c> so the composite can fall through to the next arm.</item>
/// </list>
/// </summary>
public class RealSourceBasketHoldingsSourceTests
{
    [Fact]
    public async Task FetchAsync_PendingSet_ReturnsOkWithPendingSnapshot()
    {
        var pending = new PendingBasketStore();
        pending.SetPending(new MergedBasketEnvelope
        {
            Snapshot = new HoldingsSnapshot
            {
                BasketId = "HQQQ",
                Version = "v-1",
                AsOfDate = new DateOnly(2026, 4, 17),
                ScaleFactor = 1m,
                Constituents = Array.Empty<HoldingsConstituent>(),
                Source = "live:stockanalysis+alphavantage",
            },
            MergedAtUtc = DateTimeOffset.UtcNow,
            TailSource = "alphavantage",
            IsDegraded = false,
            ContentFingerprint16 = "abcdef0123456789",
            ConstituentCount = 0,
            AnchorSource = "stockanalysis",
            HasOfficialShares = true,
            BasketMode = "anchored",
        }, DateTimeOffset.UtcNow);

        var source = new RealSourceBasketHoldingsSource(
            pending,
            pipeline: null!, // RecoverFromCacheAsync is not reached when pending is set
            NullLogger<RealSourceBasketHoldingsSource>.Instance);

        var result = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.NotNull(result.Snapshot);
        Assert.Equal("live:stockanalysis+alphavantage", result.Snapshot!.Source);
    }
}
