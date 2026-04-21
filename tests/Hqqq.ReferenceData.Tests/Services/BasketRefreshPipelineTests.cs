using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;
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
/// re-publish, publish-failure degradation, source-level failure modes,
/// and the Phase-2-native corporate-action adjustment integration.
/// </summary>
public class BasketRefreshPipelineTests
{
    [Fact]
    public async Task RefreshAsync_FirstRefresh_ActivatesAndPublishes()
    {
        var bench = PipelineBuilder.Build();
        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Null(result.PreviousFingerprint);
        Assert.Equal(60, result.ConstituentCount);
        Assert.NotNull(bench.Store.Current);
        Assert.Single(bench.Publisher.Published);

        var ev = bench.Publisher.Published[0];
        Assert.Equal(60, ev.ConstituentCount);
        Assert.Equal(60, ev.Constituents.Count);
        Assert.Equal(result.Fingerprint, ev.Fingerprint);
        // Adjustment summary is always populated on the wire now.
        Assert.NotNull(ev.AdjustmentSummary);
        Assert.Equal(0, ev.AdjustmentSummary!.SplitsApplied);
        Assert.Equal(0, ev.AdjustmentSummary.RenamesApplied);
    }

    [Fact]
    public async Task RefreshAsync_UnchangedFingerprint_DoesNotRepublish()
    {
        var bench = PipelineBuilder.Build();
        var snap = SnapshotBuilder.Build(count: 60);
        bench.Source.Enqueue(HoldingsFetchResult.Ok(snap));
        bench.Source.Enqueue(HoldingsFetchResult.Ok(snap));

        var first = await bench.Pipeline.RefreshAsync(CancellationToken.None);
        var second = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(first.Changed);
        Assert.True(second.Success);
        Assert.False(second.Changed);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, second.PreviousFingerprint);
        Assert.Single(bench.Publisher.Published);
    }

    [Fact]
    public async Task RefreshAsync_WhenContentChanges_RepublishesWithNewFingerprint()
    {
        var bench = PipelineBuilder.Build();
        var v1 = SnapshotBuilder.Build(count: 60);
        var v2 = SnapshotBuilder.Build(count: 61);

        bench.Source.Enqueue(HoldingsFetchResult.Ok(v1));
        bench.Source.Enqueue(HoldingsFetchResult.Ok(v2));

        var first = await bench.Pipeline.RefreshAsync(CancellationToken.None);
        var second = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(second.Success);
        Assert.True(second.Changed);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, second.PreviousFingerprint);
        Assert.Equal(2, bench.Publisher.Published.Count);
        Assert.Equal(61, bench.Publisher.Published[1].ConstituentCount);

        // Transition was detected (1 symbol added: SYM060).
        var summary = bench.Publisher.Published[1].AdjustmentSummary;
        Assert.NotNull(summary);
        Assert.Equal(new[] { "SYM060" }, summary!.AddedSymbols.ToArray());
        Assert.Empty(summary.RemovedSymbols);
    }

    [Fact]
    public async Task RefreshAsync_WhenPublisherThrows_StillActivatesAndReturnsSuccess()
    {
        var bench = PipelineBuilder.Build();
        bench.Publisher.ThrowOnPublish = new InvalidOperationException("broker down");

        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.NotNull(bench.Store.Current);
        Assert.Empty(bench.Publisher.Published);
    }

    [Fact]
    public async Task RefreshAsync_WhenSourceUnavailable_ReturnsFailureWithoutChangingStore()
    {
        var bench = PipelineBuilder.Build();
        bench.Source.Enqueue(HoldingsFetchResult.Unavailable("down"));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Null(bench.Store.Current);
        Assert.Empty(bench.Publisher.Published);
        Assert.Contains("Unavailable", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_WhenSourceThrows_ReturnsFailureWithoutCrashing()
    {
        var throwingSource = new ThrowingSource();
        var bench = PipelineBuilder.Build(
            source: null,
            options: new ReferenceDataOptions
            {
                Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
            });

        // Re-wire with a throwing source directly — we can't stuff a
        // throwing fetch into StubHoldingsSource.
        var opts = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 1, MaxConstituents = 500 },
        });
        var validator = new HoldingsValidator(opts);
        var adjustments = new CorporateActionAdjustmentService(
            new StubCorporateActionProvider(), opts, NullLogger<CorporateActionAdjustmentService>.Instance);
        var transition = new BasketTransitionPlanner();
        var store = new ActiveBasketStore();
        var publisher = new CapturingPublisher();
        var publishHealth = new PublishHealthTracker();
        var pipeline = new BasketRefreshPipeline(
            throwingSource, validator, adjustments, transition, store, publisher, publishHealth,
            NullLogger<BasketRefreshPipeline>.Instance);

        var result = await pipeline.RefreshAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("source threw", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_WhenValidationBlocks_DoesNotActivateOrPublish()
    {
        var bench = PipelineBuilder.Build(options: new ReferenceDataOptions
        {
            Validation = new ValidationOptions { Strict = true, MinConstituents = 50, MaxConstituents = 150 },
        });

        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 10)));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(bench.Store.Current);
        Assert.Empty(bench.Publisher.Published);
        Assert.Contains("validation failed", result.Error);
    }

    [Fact]
    public async Task RepublishCurrentAsync_WhenEmpty_ReturnsNoActiveBasket()
    {
        var bench = PipelineBuilder.Build();
        var result = await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("no active basket yet", result.Error);
        Assert.Empty(bench.Publisher.Published);
    }

    [Fact]
    public async Task RepublishCurrentAsync_AfterActivation_RePublishesSameFingerprint()
    {
        var bench = PipelineBuilder.Build();
        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));
        var first = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        var republish = await bench.Pipeline.RepublishCurrentAsync(CancellationToken.None);

        Assert.True(republish.Success);
        Assert.False(republish.Changed);
        Assert.Equal(first.Fingerprint, republish.Fingerprint);
        Assert.Equal(first.Fingerprint, republish.PreviousFingerprint);
        Assert.Equal(2, bench.Publisher.Published.Count);
        Assert.Equal(first.Fingerprint, bench.Publisher.Published[1].Fingerprint);
    }

    [Fact]
    public async Task RefreshAsync_AppliesSplitFromCorpActionFeed()
    {
        var provider = new StubCorporateActionProvider
        {
            Splits = new[]
            {
                new SplitEvent
                {
                    Symbol = "SYM001",
                    EffectiveDate = new DateOnly(2026, 4, 17),
                    Factor = 4m,
                    Description = "4:1 forward split",
                    Source = "stub",
                },
            },
            SourceTag = "stub-file",
        };

        var bench = PipelineBuilder.Build(corpActionProvider: provider);
        bench.Source.Enqueue(HoldingsFetchResult.Ok(
            SnapshotBuilder.Build(count: 60, asOfDate: new DateOnly(2026, 4, 15))));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(result.Success);
        var ev = bench.Publisher.Published.Single();
        var summary = ev.AdjustmentSummary!;
        Assert.Equal(1, summary.SplitsApplied);

        // SYM001 original shares = 101 (100 + index 1). After 4x split, 404.
        var sym1 = ev.Constituents.First(c => c.Symbol == "SYM001");
        Assert.Equal(404m, sym1.SharesHeld);
        Assert.EndsWith("+corp-adjusted", ev.Source);
    }

    [Fact]
    public async Task RefreshAsync_AppliesRenameFromCorpActionFeed()
    {
        var provider = new StubCorporateActionProvider
        {
            Renames = new[]
            {
                new SymbolRenameEvent
                {
                    OldSymbol = "SYM002",
                    NewSymbol = "NEWSYM",
                    EffectiveDate = new DateOnly(2026, 4, 17),
                    Source = "stub",
                },
            },
        };

        var bench = PipelineBuilder.Build(corpActionProvider: provider);
        bench.Source.Enqueue(HoldingsFetchResult.Ok(
            SnapshotBuilder.Build(count: 60, asOfDate: new DateOnly(2026, 4, 15))));

        var result = await bench.Pipeline.RefreshAsync(CancellationToken.None);

        Assert.True(result.Success);
        var ev = bench.Publisher.Published.Single();
        Assert.Equal(1, ev.AdjustmentSummary!.RenamesApplied);
        Assert.Contains(ev.Constituents, c => c.Symbol == "NEWSYM");
        Assert.DoesNotContain(ev.Constituents, c => c.Symbol == "SYM002");
    }

    private sealed class ThrowingSource : IHoldingsSource
    {
        public string Name => "throwing";
        public Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
            => throw new InvalidOperationException("network oops");
    }
}
