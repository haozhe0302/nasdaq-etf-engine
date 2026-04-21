using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;

namespace Hqqq.ReferenceData.Tests.Services;

/// <summary>
/// <see cref="BasketService"/> is the thin facade the REST layer calls.
/// These tests pin the null-before-first-refresh behavior and the
/// snapshot-to-DTO projection invariants (including the corporate-action
/// adjustment report that the REST surface now exposes).
/// </summary>
public class BasketServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_BeforeFirstRefresh()
    {
        var bench = PipelineBuilder.Build();
        var svc = new BasketService(bench.Store, bench.Pipeline, bench.PublishHealth);
        Assert.Null(await svc.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_AfterRefresh_ReturnsProjectedActiveBasket()
    {
        var bench = PipelineBuilder.Build();
        var svc = new BasketService(bench.Store, bench.Pipeline, bench.PublishHealth);

        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 75, source: "live:file")));

        var refresh = await svc.RefreshAsync(CancellationToken.None);
        Assert.True(refresh.Success);

        var current = await svc.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal("HQQQ", current!.Active.BasketId);
        Assert.Equal("live:file", current.Source);
        Assert.Equal(75, current.Active.ConstituentCount);
        Assert.Equal(75, current.Constituents.Count);
        Assert.All(current.Constituents, c => Assert.Equal("live:file", c.SharesOrigin));

        // Empty but non-null adjustment report now surfaces on the facade.
        Assert.NotNull(current.LatestAdjustmentReport);
        Assert.Equal(0, current.LatestAdjustmentReport!.SplitsApplied);
        Assert.Equal(0, current.LatestAdjustmentReport.RenamesApplied);
    }

    [Fact]
    public async Task RefreshAsync_SurfacesPipelineResult()
    {
        var bench = PipelineBuilder.Build();
        var svc = new BasketService(bench.Store, bench.Pipeline, bench.PublishHealth);

        bench.Source.Enqueue(HoldingsFetchResult.Unavailable("no-live"));

        var result = await svc.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unavailable", result.Error);
    }
}
