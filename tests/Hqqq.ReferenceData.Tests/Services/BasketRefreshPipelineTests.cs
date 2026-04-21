using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Services;

/// <summary>
/// The refresh pipeline is the heart of the service. These tests exercise
/// the full lifecycle through test doubles: first-refresh activation,
/// unchanged fingerprint idempotency, real content change triggering
/// re-publish, publish-failure degradation, and source-level failure
/// modes.
/// </summary>
public class BasketRefreshPipelineTests
{
    [Fact]
    public async Task RefreshAsync_FirstRefresh_ActivatesAndPublishes()
    {
        var (pipeline, source, publisher, store) = BuildPipeline();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var result = await pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Null(result.PreviousFingerprint);
        Assert.Equal(60, result.ConstituentCount);
        Assert.NotNull(store.Current);
        Assert.Single(publisher.Published);

        var ev = publisher.Published[0];
        Assert.Equal(60, ev.ConstituentCount);
        Assert.Equal(60, ev.Constituents.Count);
        Assert.Equal(result.Fingerprint, ev.Fingerprint);
    }

    [Fact]
    public async Task RefreshAsync_UnchangedFingerprint_DoesNotRepublish()
    {
        var (pipeline, source, publisher, store) = BuildPipeline();
        var snap = SnapshotBuilder.Build(count: 60);
        source.Enqueue(HoldingsFetchResult.Ok(snap));
        source.Enqueue(HoldingsFetchResult.Ok(snap));

        var first = await pipeline.RefreshAsync(CancellationToken.None);
        var second = await pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(first.Changed);
        Assert.True(second.Success);
        Assert.False(second.Changed);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, second.PreviousFingerprint);
        // Only the first activation published; idempotency guard holds.
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task RefreshAsync_WhenContentChanges_RepublishesWithNewFingerprint()
    {
        var (pipeline, source, publisher, _) = BuildPipeline();
        var v1 = SnapshotBuilder.Build(count: 60);
        var v2 = SnapshotBuilder.Build(count: 61);

        source.Enqueue(HoldingsFetchResult.Ok(v1));
        source.Enqueue(HoldingsFetchResult.Ok(v2));

        var first = await pipeline.RefreshAsync(CancellationToken.None);
        var second = await pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(second.Success);
        Assert.True(second.Changed);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, second.PreviousFingerprint);
        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(61, publisher.Published[1].ConstituentCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenPublisherThrows_StillActivatesAndReturnsSuccess()
    {
        var (pipeline, source, publisher, store) = BuildPipeline();
        publisher.ThrowOnPublish = new InvalidOperationException("broker down");

        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var result = await pipeline.RefreshAsync(CancellationToken.None);

        // Publish failure must not fail the refresh — active basket is
        // the in-memory truth; the next republish tick retries.
        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.NotNull(store.Current);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RefreshAsync_WhenSourceUnavailable_ReturnsFailureWithoutChangingStore()
    {
        var (pipeline, source, publisher, store) = BuildPipeline();
        source.Enqueue(HoldingsFetchResult.Unavailable("down"));

        var result = await pipeline.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Null(store.Current);
        Assert.Empty(publisher.Published);
        Assert.Contains("Unavailable", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_WhenSourceThrows_ReturnsFailureWithoutCrashing()
    {
        var throwingSource = new ThrowingSource();
        var options = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
        });
        var validator = new HoldingsValidator(options);
        var store = new ActiveBasketStore();
        var publisher = new CapturingPublisher();
        var publishHealth = new PublishHealthTracker();
        var pipeline = new BasketRefreshPipeline(
            throwingSource, validator, store, publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance);

        var result = await pipeline.RefreshAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("source threw", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_WhenValidationBlocks_DoesNotActivateOrPublish()
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 50, MaxConstituents = 150 },
        });
        var validator = new HoldingsValidator(options);
        var store = new ActiveBasketStore();
        var publisher = new CapturingPublisher();
        var publishHealth = new PublishHealthTracker();
        var source = new StubHoldingsSource();
        var pipeline = new BasketRefreshPipeline(
            source, validator, store, publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance);

        // Only 10 constituents — below MinConstituents=50, validator blocks.
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 10)));

        var result = await pipeline.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(store.Current);
        Assert.Empty(publisher.Published);
        Assert.Contains("validation failed", result.Error);
    }

    [Fact]
    public async Task RepublishCurrentAsync_WhenEmpty_ReturnsNoActiveBasket()
    {
        var (pipeline, _, publisher, _) = BuildPipeline();
        var result = await pipeline.RepublishCurrentAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("no active basket yet", result.Error);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RepublishCurrentAsync_AfterActivation_RePublishesSameFingerprint()
    {
        var (pipeline, source, publisher, _) = BuildPipeline();
        source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));
        var first = await pipeline.RefreshAsync(CancellationToken.None);

        var republish = await pipeline.RepublishCurrentAsync(CancellationToken.None);

        Assert.True(republish.Success);
        Assert.False(republish.Changed);
        Assert.Equal(first.Fingerprint, republish.Fingerprint);
        Assert.Equal(first.Fingerprint, republish.PreviousFingerprint);
        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(first.Fingerprint, publisher.Published[1].Fingerprint);
    }

    private static (BasketRefreshPipeline Pipeline, StubHoldingsSource Source, CapturingPublisher Publisher, ActiveBasketStore Store)
        BuildPipeline()
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
        return (pipeline, source, publisher, store);
    }

    private sealed class ThrowingSource : IHoldingsSource
    {
        public string Name => "throwing";
        public Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
            => throw new InvalidOperationException("network oops");
    }
}
