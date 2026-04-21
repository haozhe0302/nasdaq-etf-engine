using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;

namespace Hqqq.ReferenceData.Tests.Sources;

/// <summary>
/// <see cref="HoldingsFingerprint.Compute"/> must be deterministic across
/// invocations, stable against order/lineage changes, and sensitive to
/// actual content drift. Those three invariants are what the engine's
/// idempotency guard relies on.
/// </summary>
public class HoldingsFingerprintTests
{
    [Fact]
    public void Compute_IsStableAcrossInvocations()
    {
        var snapshot = SnapshotBuilder.Build();
        Assert.Equal(
            HoldingsFingerprint.Compute(snapshot),
            HoldingsFingerprint.Compute(snapshot));
    }

    [Fact]
    public void Compute_IsStableAcrossConstituentOrder()
    {
        var a = SnapshotBuilder.Build();
        var b = a with
        {
            Constituents = a.Constituents
                .OrderByDescending(c => c.Symbol, StringComparer.Ordinal)
                .ToArray(),
        };

        Assert.Equal(HoldingsFingerprint.Compute(a), HoldingsFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_IsStableAcrossSourceChanges()
    {
        var a = SnapshotBuilder.Build(source: "live:http");
        var b = a with { Source = "fallback-seed" };

        // Lineage metadata must not affect the fingerprint — otherwise the
        // engine would re-pick-up the same basket every time the live/seed
        // arm flips.
        Assert.Equal(HoldingsFingerprint.Compute(a), HoldingsFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_ChangesWhenReferencePriceChanges()
    {
        var a = SnapshotBuilder.Build();
        var b = a with
        {
            Constituents = a.Constituents
                .Select((c, i) => i == 0 ? c with { ReferencePrice = c.ReferencePrice + 1m } : c)
                .ToArray(),
        };

        Assert.NotEqual(HoldingsFingerprint.Compute(a), HoldingsFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_ChangesWhenAConstituentIsAdded()
    {
        var a = SnapshotBuilder.Build(count: 60);
        var b = SnapshotBuilder.Build(count: 61);
        Assert.NotEqual(HoldingsFingerprint.Compute(a), HoldingsFingerprint.Compute(b));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public void Compute_ProducesStableHashForNearHundredCounts(int count)
    {
        // The service targets ~100 names but must handle drift (99/100/101).
        // We just assert the output is a 64-char SHA-256 hex string and is
        // self-consistent on repeat.
        var snapshot = SnapshotBuilder.Build(count: count);
        var fp = HoldingsFingerprint.Compute(snapshot);
        Assert.Equal(64, fp.Length);
        Assert.Equal(fp, HoldingsFingerprint.Compute(snapshot));
    }
}
