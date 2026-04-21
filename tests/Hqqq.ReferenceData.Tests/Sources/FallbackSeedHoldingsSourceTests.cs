using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.ReferenceData.Tests.Sources;

public class FallbackSeedHoldingsSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsOk_WithFallbackSeedSource()
    {
        var loader = BasketSeedLoaderTests.BuildLoader(seedPath: null);
        var source = new FallbackSeedHoldingsSource(loader, NullLogger<FallbackSeedHoldingsSource>.Instance);

        var result = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
        // Soft lower bound: committed seed ships ~100; assert >= 90 so the
        // test is not brittle to small drift.
        Assert.True(result.Snapshot.Constituents.Count >= 90,
            $"expected at least 90 constituents, got {result.Snapshot.Constituents.Count}");
    }

    [Fact]
    public async Task FetchAsync_ReturnsSameSnapshotOnRepeatedCalls()
    {
        var loader = BasketSeedLoaderTests.BuildLoader(seedPath: null);
        var source = new FallbackSeedHoldingsSource(loader, NullLogger<FallbackSeedHoldingsSource>.Instance);

        var a = await source.FetchAsync(CancellationToken.None);
        var b = await source.FetchAsync(CancellationToken.None);

        // Same reference — loader is called once at construction.
        Assert.Same(a.Snapshot, b.Snapshot);
    }
}
