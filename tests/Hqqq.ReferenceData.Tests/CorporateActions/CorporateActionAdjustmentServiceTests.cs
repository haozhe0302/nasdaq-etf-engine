using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hqqq.ReferenceData.Tests.CorporateActions;

public class CorporateActionAdjustmentServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 4, 15);
    private static readonly DateTimeOffset Runtime = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AdjustAsync_ForwardSplit_DoublesShares()
    {
        var provider = new StubCorporateActionProvider
        {
            Splits = new[]
            {
                Split("SYM001", new DateOnly(2026, 4, 17), 2m),
            },
        };

        var svc = BuildService(provider);
        var snapshot = SnapshotBuilder.Build(count: 5, asOfDate: AsOf);
        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        // SYM001 original shares = 101 (100 + 1). After 2x, 202.
        var sym1 = result.Snapshot.Constituents.Single(c => c.Symbol == "SYM001");
        Assert.Equal(202m, sym1.SharesHeld);
        Assert.Equal(1, result.Report.SplitsApplied);
        Assert.EndsWith("+corp-adjusted", result.Snapshot.Source);
    }

    [Fact]
    public async Task AdjustAsync_ReverseSplit_ReducesShares()
    {
        var provider = new StubCorporateActionProvider
        {
            Splits = new[]
            {
                Split("SYM002", new DateOnly(2026, 4, 18), 0.1m),
            },
        };

        var svc = BuildService(provider);
        var snapshot = SnapshotBuilder.Build(count: 5, asOfDate: AsOf);
        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        // SYM002 original shares = 102. After 1:10 reverse split, 10.2.
        var sym2 = result.Snapshot.Constituents.Single(c => c.Symbol == "SYM002");
        Assert.Equal(10.2m, sym2.SharesHeld);
        Assert.Equal(1, result.Report.SplitsApplied);
    }

    [Fact]
    public async Task AdjustAsync_Rename_RemapsSymbolField()
    {
        var provider = new StubCorporateActionProvider
        {
            Renames = new[]
            {
                new SymbolRenameEvent
                {
                    OldSymbol = "SYM003",
                    NewSymbol = "NEWCO",
                    EffectiveDate = new DateOnly(2026, 4, 17),
                    Source = "test",
                },
            },
        };

        var svc = BuildService(provider);
        var snapshot = SnapshotBuilder.Build(count: 5, asOfDate: AsOf);
        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        Assert.DoesNotContain(result.Snapshot.Constituents, c => c.Symbol == "SYM003");
        Assert.Contains(result.Snapshot.Constituents, c => c.Symbol == "NEWCO");
        Assert.Equal(1, result.Report.RenamesApplied);
    }

    [Fact]
    public async Task AdjustAsync_NoEvents_ReturnsOriginalSnapshot()
    {
        var svc = BuildService(new StubCorporateActionProvider());
        var snapshot = SnapshotBuilder.Build(count: 10, asOfDate: AsOf);

        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        Assert.Same(snapshot, result.Snapshot);
        Assert.Equal(0, result.Report.SplitsApplied);
        Assert.Equal(0, result.Report.RenamesApplied);
    }

    [Fact]
    public async Task AdjustAsync_EventBeforeAsOfDate_IsIgnored()
    {
        var provider = new StubCorporateActionProvider
        {
            // Effective date == AsOfDate should be ignored (lower bound exclusive).
            Splits = new[] { Split("SYM001", AsOf, 4m) },
        };

        var svc = BuildService(provider);
        var snapshot = SnapshotBuilder.Build(count: 3, asOfDate: AsOf);
        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        var sym1 = result.Snapshot.Constituents.Single(c => c.Symbol == "SYM001");
        Assert.Equal(101m, sym1.SharesHeld); // unchanged
        Assert.Equal(0, result.Report.SplitsApplied);
    }

    [Fact]
    public async Task AdjustAsync_RuntimeDateBeforeOrEqualAsOfDate_NoAdjustment()
    {
        var provider = new StubCorporateActionProvider
        {
            Splits = new[] { Split("SYM001", new DateOnly(2026, 4, 10), 2m) },
        };

        // Set clock to AsOfDate — runtime date <= AsOf, window is empty.
        var clock = new FakeTimeProvider(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var svc = BuildService(provider, clock);

        var result = await svc.AdjustAsync(SnapshotBuilder.Build(count: 3, asOfDate: AsOf), CancellationToken.None);

        Assert.Equal(0, result.Report.SplitsApplied);
    }

    [Fact]
    public async Task AdjustAsync_CumulativeFactorAcrossMultipleSplits()
    {
        var provider = new StubCorporateActionProvider
        {
            Splits = new[]
            {
                Split("SYM001", new DateOnly(2026, 4, 16), 2m),
                Split("SYM001", new DateOnly(2026, 4, 18), 3m),
            },
        };

        var svc = BuildService(provider);
        var snapshot = SnapshotBuilder.Build(count: 3, asOfDate: AsOf);
        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        // 101 * 2 * 3 = 606
        var sym1 = result.Snapshot.Constituents.Single(c => c.Symbol == "SYM001");
        Assert.Equal(606m, sym1.SharesHeld);
        var adj = result.Report.SplitAdjustments.Single();
        Assert.Equal(6m, adj.CumulativeFactor);
        Assert.Equal(2, adj.AppliedSplits.Count);
    }

    [Fact]
    public async Task AdjustAsync_ProviderThrows_FallsThroughWithoutChange()
    {
        var svc = BuildService(new ThrowingProvider());
        var snapshot = SnapshotBuilder.Build(count: 3, asOfDate: AsOf);

        var result = await svc.AdjustAsync(snapshot, CancellationToken.None);

        Assert.Same(snapshot, result.Snapshot);
        Assert.NotNull(result.Report.ProviderError);
    }

    private static CorporateActionAdjustmentService BuildService(
        ICorporateActionProvider provider,
        FakeTimeProvider? clock = null)
    {
        clock ??= new FakeTimeProvider(Runtime);
        var options = Options.Create(new ReferenceDataOptions
        {
            CorporateActions = new CorporateActionOptions { LookbackDays = 365 },
        });
        return new CorporateActionAdjustmentService(
            provider,
            options,
            NullLogger<CorporateActionAdjustmentService>.Instance,
            clock);
    }

    private static SplitEvent Split(string symbol, DateOnly date, decimal factor) => new()
    {
        Symbol = symbol,
        EffectiveDate = date,
        Factor = factor,
        Source = "test",
    };

    private sealed class ThrowingProvider : ICorporateActionProvider
    {
        public string Name => "throwing";
        public Task<CorporateActionFeed> FetchAsync(
            IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct)
            => throw new InvalidOperationException("upstream down");
    }
}
