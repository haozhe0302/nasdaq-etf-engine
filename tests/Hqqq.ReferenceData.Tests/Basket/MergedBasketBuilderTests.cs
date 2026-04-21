using Hqqq.ReferenceData.Basket;

namespace Hqqq.ReferenceData.Tests.Basket;

/// <summary>
/// Covers the Phase-2-native <see cref="MergedBasketBuilder"/> ported
/// from the Phase 1 Basket module: weight normalization, universe
/// guardrail, fingerprint stability.
/// </summary>
public class MergedBasketBuilderTests
{
    private static readonly DateOnly AsOf = new(2026, 4, 17);

    [Fact]
    public void Build_NormalizesWeightsToOne_AndSortsDeterministically()
    {
        var tail = new MergedBasketBuilder.TailBlock(
            new[]
            {
                new MergedBasketBuilder.TailEntry("AAPL", "Apple", 10m, "Technology"),
                new MergedBasketBuilder.TailEntry("MSFT", "Microsoft", 20m, "Technology"),
                new MergedBasketBuilder.TailEntry("NVDA", "NVIDIA", 70m, "Technology"),
            },
            SourceName: "alphavantage",
            IsProxy: false,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.Build(tail, universeSymbols: null,
            basketId: "HQQQ", version: "v-test");

        Assert.Equal(3, result.Quality.FinalSymbolCount);
        // Weights sum ~= 1.0 within rounding.
        Assert.Equal(1.0m, Math.Round(result.Snapshot.Constituents.Sum(c => c.TargetWeight ?? 0m), 6));
        // Fingerprint is deterministic: re-running yields the same hash.
        var again = MergedBasketBuilder.Build(tail, null, "HQQQ", "v-test");
        Assert.Equal(result.Quality.ContentFingerprint16, again.Quality.ContentFingerprint16);
    }

    [Fact]
    public void Build_UniverseGuardrail_DropsOutOfUniverseTickers()
    {
        var tail = new MergedBasketBuilder.TailBlock(
            new[]
            {
                new MergedBasketBuilder.TailEntry("AAPL", "Apple", 50m, "Technology"),
                new MergedBasketBuilder.TailEntry("BOGUS", "NotInUniverse", 50m, "Unknown"),
            },
            SourceName: "alphavantage",
            IsProxy: false,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.Build(
            tail,
            universeSymbols: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            basketId: "HQQQ",
            version: "v-test");

        Assert.Single(result.Snapshot.Constituents);
        Assert.Equal("AAPL", result.Snapshot.Constituents[0].Symbol);
        Assert.Equal(1, result.Quality.UniverseDroppedCount);
    }

    [Fact]
    public void Build_ProxyTail_EmitsDegradedLineage()
    {
        var tail = new MergedBasketBuilder.TailBlock(
            new[] { new MergedBasketBuilder.TailEntry("AAPL", "Apple", 100m, "Unknown") },
            SourceName: "nasdaq",
            IsProxy: true,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.Build(tail, null, "HQQQ", "v-test");

        Assert.True(result.Quality.IsDegraded);
        Assert.StartsWith("live:nasdaq:proxy", result.Snapshot.Source);
    }

    [Fact]
    public void ComputeContentFingerprint16_IsStable_AcrossPermutations()
    {
        var a = new[]
        {
            new Hqqq.ReferenceData.Sources.HoldingsConstituent
            {
                Symbol = "MSFT", Name = "Microsoft", Sector = "Technology",
                SharesHeld = 0m, ReferencePrice = 0m, TargetWeight = 0.5m,
            },
            new Hqqq.ReferenceData.Sources.HoldingsConstituent
            {
                Symbol = "AAPL", Name = "Apple", Sector = "Technology",
                SharesHeld = 0m, ReferencePrice = 0m, TargetWeight = 0.5m,
            },
        };
        var b = a.Reverse().ToArray();

        Assert.Equal(
            MergedBasketBuilder.ComputeContentFingerprint16(a, AsOf),
            MergedBasketBuilder.ComputeContentFingerprint16(b, AsOf));
    }
}
