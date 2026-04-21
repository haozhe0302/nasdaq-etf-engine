using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Services;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;

namespace Hqqq.ReferenceData.Tests.CorporateActions;

public class BasketTransitionPlannerTests
{
    [Fact]
    public void Plan_FirstActivation_NoTransition()
    {
        var planner = new BasketTransitionPlanner();
        var snapshot = SnapshotBuilder.Build(count: 60);
        var baseline = AdjustmentReport.Empty(
            "test", snapshot.AsOfDate, new DateOnly(2026, 4, 20), DateTimeOffset.UtcNow);

        var (outSnapshot, report) = planner.Plan(previous: null, snapshot: snapshot, baseline: baseline);

        Assert.Same(snapshot, outSnapshot);
        Assert.Empty(report.AddedSymbols);
        Assert.Empty(report.RemovedSymbols);
        Assert.False(report.ScaleFactorRecalibrated);
    }

    [Fact]
    public void Plan_DetectsAddedAndRemovedSymbols()
    {
        var planner = new BasketTransitionPlanner();
        var prevSnapshot = SnapshotBuilder.Build(count: 3);
        var previous = new ActiveBasket
        {
            Snapshot = prevSnapshot,
            Fingerprint = "fp-prev",
            ActivatedAtUtc = DateTimeOffset.UtcNow,
        };

        // Build a new snapshot: drop SYM000, add NEW001.
        var newConstituents = prevSnapshot.Constituents
            .Where(c => c.Symbol != "SYM000")
            .Concat(new[]
            {
                new HoldingsConstituent
                {
                    Symbol = "NEW001",
                    Name = "New Corp",
                    Sector = "Technology",
                    SharesHeld = 10m,
                    ReferencePrice = 50m,
                    TargetWeight = 0.1m,
                },
            })
            .ToArray();
        var newSnapshot = prevSnapshot with { Constituents = newConstituents };

        var baseline = AdjustmentReport.Empty(
            "test", prevSnapshot.AsOfDate, new DateOnly(2026, 4, 20), DateTimeOffset.UtcNow);

        var (_, report) = planner.Plan(previous, newSnapshot, baseline);

        Assert.Equal(new[] { "NEW001" }, report.AddedSymbols);
        Assert.Equal(new[] { "SYM000" }, report.RemovedSymbols);
    }

    [Fact]
    public void Plan_RecalibratesScaleFactorWhenRawValueChanges()
    {
        var planner = new BasketTransitionPlanner();

        // Previous: 2 symbols worth $1000 total notional, scale = 2.0.
        var prevSnapshot = SnapshotBuilder.Build(count: 2) with
        {
            ScaleFactor = 2.0m,
        };
        var previous = new ActiveBasket
        {
            Snapshot = prevSnapshot,
            Fingerprint = "fp-prev",
            ActivatedAtUtc = DateTimeOffset.UtcNow,
        };

        // New snapshot doubles one constituent's shares (e.g. post-split).
        var mutated = prevSnapshot.Constituents.ToList();
        mutated[0] = mutated[0] with { SharesHeld = mutated[0].SharesHeld * 2m };
        var newSnapshot = prevSnapshot with { Constituents = mutated };

        var baseline = AdjustmentReport.Empty(
            "test", prevSnapshot.AsOfDate, new DateOnly(2026, 4, 20), DateTimeOffset.UtcNow);

        var (outSnapshot, report) = planner.Plan(previous, newSnapshot, baseline);

        Assert.True(report.ScaleFactorRecalibrated);
        Assert.NotEqual(prevSnapshot.ScaleFactor, outSnapshot.ScaleFactor);
        Assert.Equal(prevSnapshot.ScaleFactor, report.PreviousScaleFactor);
        Assert.Equal(outSnapshot.ScaleFactor, report.NewScaleFactor);

        // Continuity invariant: oldScale * oldRawValue ≈ newScale * newRawValue.
        var oldRaw = prevSnapshot.Constituents.Sum(c => c.SharesHeld * c.ReferencePrice);
        var newRaw = outSnapshot.Constituents.Sum(c => c.SharesHeld * c.ReferencePrice);
        var oldNav = prevSnapshot.ScaleFactor * oldRaw;
        var newNav = outSnapshot.ScaleFactor * newRaw;
        Assert.Equal(oldNav, newNav);
    }

    [Fact]
    public void Plan_NoRawValueChange_DoesNotRecalibrate()
    {
        var planner = new BasketTransitionPlanner();
        var prevSnapshot = SnapshotBuilder.Build(count: 3) with { ScaleFactor = 1.5m };
        var previous = new ActiveBasket
        {
            Snapshot = prevSnapshot,
            Fingerprint = "fp-prev",
            ActivatedAtUtc = DateTimeOffset.UtcNow,
        };

        var baseline = AdjustmentReport.Empty(
            "test", prevSnapshot.AsOfDate, new DateOnly(2026, 4, 20), DateTimeOffset.UtcNow);

        var (outSnapshot, report) = planner.Plan(previous, prevSnapshot, baseline);

        Assert.False(report.ScaleFactorRecalibrated);
        Assert.Equal(1.5m, outSnapshot.ScaleFactor);
    }
}
