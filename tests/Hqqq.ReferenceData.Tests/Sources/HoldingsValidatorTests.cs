using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Sources;

/// <summary>
/// Validation must reject structurally broken snapshots (empty, duplicate
/// symbols, count out of bounds) under both strict and permissive policy;
/// per-row issues (bad name/sector/shares/price) block only in strict mode.
/// </summary>
public class HoldingsValidatorTests
{
    [Fact]
    public void Validate_AcceptsWellFormedSnapshot()
    {
        var validator = BuildValidator(strict: true);
        var outcome = validator.Validate(SnapshotBuilder.Build(count: 60));
        Assert.True(outcome.IsValid);
        Assert.False(validator.BlocksActivation(outcome));
    }

    [Fact]
    public void Validate_RejectsDuplicateSymbols_InBothModes()
    {
        var snapshot = SnapshotBuilder.Build(count: 2);
        var dupSymbol = snapshot.Constituents[0].Symbol;
        var withDup = snapshot with
        {
            Constituents = snapshot.Constituents
                .Concat(new[] { snapshot.Constituents[1] with { Symbol = dupSymbol } })
                .ToArray(),
        };

        foreach (var strict in new[] { true, false })
        {
            var v = BuildValidator(strict: strict, min: 1, max: 10);
            var outcome = v.Validate(withDup);
            Assert.False(outcome.IsValid);
            Assert.True(v.BlocksActivation(outcome));
            Assert.Contains(outcome.Errors, e => e.Contains("duplicate symbol", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_RejectsEmptyUniverse_InBothModes()
    {
        var empty = SnapshotBuilder.Build(count: 60) with { Constituents = Array.Empty<HoldingsConstituent>() };

        foreach (var strict in new[] { true, false })
        {
            var v = BuildValidator(strict: strict);
            var outcome = v.Validate(empty);
            Assert.True(v.BlocksActivation(outcome));
        }
    }

    [Fact]
    public void Validate_RejectsCountBelowMin_InBothModes()
    {
        var small = SnapshotBuilder.Build(count: 10);
        foreach (var strict in new[] { true, false })
        {
            var v = BuildValidator(strict: strict, min: 50, max: 150);
            var outcome = v.Validate(small);
            Assert.True(v.BlocksActivation(outcome));
        }
    }

    [Fact]
    public void Validate_RejectsCountAboveMax_InBothModes()
    {
        var huge = SnapshotBuilder.Build(count: 200);
        foreach (var strict in new[] { true, false })
        {
            var v = BuildValidator(strict: strict, min: 50, max: 150);
            var outcome = v.Validate(huge);
            Assert.True(v.BlocksActivation(outcome));
        }
    }

    [Fact]
    public void Validate_PermissiveTolerates_PerRowDataQualityIssues()
    {
        var snap = SnapshotBuilder.Build(count: 60);
        var bad = snap with
        {
            Constituents = snap.Constituents
                .Select((c, i) => i == 0 ? c with { Sector = string.Empty } : c)
                .ToArray(),
        };

        var strict = BuildValidator(strict: true);
        var permissive = BuildValidator(strict: false);

        Assert.False(strict.Validate(bad).IsValid);
        Assert.True(strict.BlocksActivation(strict.Validate(bad)));

        var permissiveOutcome = permissive.Validate(bad);
        Assert.False(permissiveOutcome.IsValid);
        Assert.False(permissive.BlocksActivation(permissiveOutcome));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public void Validate_AcceptsNearHundredCounts(int count)
    {
        var snap = SnapshotBuilder.Build(count: count);
        var v = BuildValidator(strict: true, min: 50, max: 150);
        var outcome = v.Validate(snap);
        Assert.True(outcome.IsValid);
        Assert.False(v.BlocksActivation(outcome));
    }

    [Fact]
    public void Validate_AcceptsAnchoredHybridSnapshot_InStrictMode()
    {
        // Mirrors the four-source anchored merge: anchor rows carry
        // real SharesSource + SharesHeld > 0 but ReferencePrice = 0
        // (price filled by pricing layer downstream); tail rows carry
        // SharesSource="unavailable" with SharesHeld = 0 + ReferencePrice = 0.
        var snap = SnapshotBuilder.Build(count: 60);
        var hybrid = snap with
        {
            Constituents = snap.Constituents
                .Select((c, i) => i < 25
                    ? c with
                    {
                        SharesHeld = 1_000m + i,
                        ReferencePrice = 0m,
                        SharesSource = "stockanalysis",
                    }
                    : c with
                    {
                        SharesHeld = 0m,
                        ReferencePrice = 0m,
                        SharesSource = HoldingsValidator.UnavailableSharesSource,
                    })
                .ToArray(),
        };

        var validator = BuildValidator(strict: true);
        var outcome = validator.Validate(hybrid);

        Assert.True(outcome.IsValid);
        Assert.False(validator.BlocksActivation(outcome));
    }

    [Fact]
    public void Validate_AnchoredRowWithZeroShares_StillFails_EvenWithLineage()
    {
        // An anchor row that claims a real SharesSource but has zero
        // shares is a data-quality bug in the upstream scraper — we
        // must NOT silently accept it under the hybrid carve-out.
        var snap = SnapshotBuilder.Build(count: 60);
        var bad = snap with
        {
            Constituents = snap.Constituents
                .Select((c, i) => i == 0
                    ? c with { SharesHeld = 0m, ReferencePrice = 0m, SharesSource = "stockanalysis" }
                    : c)
                .ToArray(),
        };

        var validator = BuildValidator(strict: true);
        var outcome = validator.Validate(bad);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, e => e.Contains("sharesHeld must be > 0", StringComparison.Ordinal));
        Assert.True(validator.BlocksActivation(outcome));
    }

    [Fact]
    public void Validate_LegacyRowWithoutLineage_StillRequiresPositivePrice()
    {
        // Snapshot rows with no SharesSource lineage (e.g. seed or
        // file-drop loaders) must continue to be held to the strict
        // shares + price > 0 rule so existing callers don't regress.
        var snap = SnapshotBuilder.Build(count: 60);
        var bad = snap with
        {
            Constituents = snap.Constituents
                .Select((c, i) => i == 0
                    ? c with { ReferencePrice = 0m, SharesSource = null }
                    : c)
                .ToArray(),
        };

        var validator = BuildValidator(strict: true);
        var outcome = validator.Validate(bad);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, e => e.Contains("referencePrice must be > 0", StringComparison.Ordinal));
        Assert.True(validator.BlocksActivation(outcome));
    }

    private static HoldingsValidator BuildValidator(bool strict, int min = 50, int max = 150)
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            Validation = new ValidationOptions
            {
                Strict = strict,
                MinConstituents = min,
                MaxConstituents = max,
            },
        });
        return new HoldingsValidator(options);
    }
}
