using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Services;

/// <summary>
/// <see cref="BasketService"/> is the thin facade the REST layer calls.
/// These tests pin the null-before-first-refresh behavior and the
/// snapshot-to-DTO projection invariants.
/// </summary>
public class BasketServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_BeforeFirstRefresh()
    {
        var (svc, _, _) = Build();
        Assert.Null(await svc.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_AfterRefresh_ReturnsProjectedActiveBasket()
    {
        var (svc, source, _) = Build();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 75, source: "live:file")));

        var refresh = await svc.RefreshAsync(CancellationToken.None);
        Assert.True(refresh.Success);

        var current = await svc.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal("HQQQ", current!.Active.BasketId);
        Assert.Equal("live:file", current.Source);
        Assert.Equal(75, current.Active.ConstituentCount);
        Assert.Equal(75, current.Constituents.Count);
        Assert.All(current.Constituents, c => Assert.Equal("live:file", c.SharesOrigin));
    }

    [Fact]
    public async Task RefreshAsync_SurfacesPipelineResult()
    {
        var (svc, source, _) = Build();
        source.Enqueue(HoldingsFetchResult.Unavailable("no-live"));

        var result = await svc.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unavailable", result.Error);
    }

    private static (BasketService Service, StubHoldingsSource Source, CapturingPublisher Publisher) Build()
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
        });
        var validator = new HoldingsValidator(options);
        var store = new ActiveBasketStore();
        var publisher = new CapturingPublisher();
        var publishHealth = new PublishHealthTracker();
        var source = new StubHoldingsSource();
        var pipeline = new BasketRefreshPipeline(
            source, validator, store, publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance);
        return (new BasketService(store, pipeline, publishHealth), source, publisher);
    }
}
