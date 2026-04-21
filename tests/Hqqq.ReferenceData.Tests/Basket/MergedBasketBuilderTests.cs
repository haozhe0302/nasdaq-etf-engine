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
    public void BuildAnchored_PreservesSharesOnAnchor_ZeroOnTail_NormalizesWeightsToOne()
    {
        // Anchor rows carry authoritative SharesHeld; tail rows carry
        // SharesHeld=0 with SharesSource="unavailable". Weights sum to
        // (anchorFractionSum + (1 - anchorFractionSum)) == 1.0.
        var anchor = new MergedBasketBuilder.AnchorBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.AnchorEntry("AAPL", "Apple", "Technology", 200_000m, 9m),
                new MergedBasketBuilder.AnchorEntry("MSFT", "Microsoft", "Technology", 150_000m, 8m),
            },
            SourceName: "stockanalysis",
            AsOfDate: AsOf);

        var tail = new MergedBasketBuilder.TailBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.TailEntry("NVDA", "NVIDIA", 6m, "Technology"),
                new MergedBasketBuilder.TailEntry("AMZN", "Amazon", 5m, "Consumer"),
                // Overlap with anchor — must be dropped.
                new MergedBasketBuilder.TailEntry("AAPL", "Apple dup", 99m, "Ignored"),
            },
            SourceName: "alphavantage",
            IsProxy: false,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.BuildAnchored(
            anchor, tail, universeSymbols: null,
            basketId: "HQQQ", version: "v-test");

        Assert.Equal(4, result.Quality.FinalSymbolCount);
        Assert.Equal(2, result.Quality.AnchorRowCount);
        Assert.Equal(2, result.Quality.TailRowCount);
        Assert.Equal("anchored", result.Quality.BasketMode);
        Assert.Equal("stockanalysis", result.Quality.AnchorSource);
        Assert.True(result.Quality.HasOfficialShares);
        Assert.False(result.Quality.IsDegraded);
        Assert.Equal("live:stockanalysis+alphavantage", result.Snapshot.Source);

        // Anchor constituents preserve real shares + lineage.
        var aapl = result.Snapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(200_000m, aapl.SharesHeld);
        Assert.Equal("stockanalysis", aapl.SharesSource);
        Assert.Equal("stockanalysis", aapl.WeightSource);

        // Tail constituents are weight-only.
        var nvda = result.Snapshot.Constituents.Single(c => c.Symbol == "NVDA");
        Assert.Equal(0m, nvda.SharesHeld);
        Assert.Equal("unavailable", nvda.SharesSource);
        Assert.Equal("alphavantage", nvda.WeightSource);

        // Total weight ≈ 1.0.
        var total = result.Snapshot.Constituents.Sum(c => c.TargetWeight ?? 0m);
        Assert.Equal(1m, Math.Round(total, 6));
    }

    [Fact]
    public void BuildAnchored_NasdaqProxyTail_EmitsDegradedLineage()
    {
        // Tail.IsProxy=true → basket is "anchored-proxy-tail" degraded.
        // Anchor rows still carry real shares.
        var anchor = new MergedBasketBuilder.AnchorBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.AnchorEntry("AAPL", "Apple", "Technology", 100m, 10m),
            },
            SourceName: "schwab",
            AsOfDate: AsOf);

        var tail = new MergedBasketBuilder.TailBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.TailEntry("MSFT", "Microsoft", 5m, "Technology"),
            },
            SourceName: "nasdaq",
            IsProxy: true,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.BuildAnchored(
            anchor, tail, universeSymbols: null,
            basketId: "HQQQ", version: "v-test");

        Assert.Equal("anchored-proxy-tail", result.Quality.BasketMode);
        Assert.True(result.Quality.IsDegraded);
        Assert.Equal("schwab", result.Quality.AnchorSource);
        Assert.True(result.Quality.HasOfficialShares);
        Assert.Equal("live:schwab+nasdaq:proxy", result.Snapshot.Source);
    }

    [Fact]
    public void BuildAnchored_AppliesUniverseGuardrailToTail()
    {
        var anchor = new MergedBasketBuilder.AnchorBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.AnchorEntry("AAPL", "Apple", "Technology", 100m, 20m),
            },
            SourceName: "stockanalysis",
            AsOfDate: AsOf);

        var tail = new MergedBasketBuilder.TailBlock(
            Entries: new[]
            {
                new MergedBasketBuilder.TailEntry("MSFT", "Microsoft", 50m, "Technology"),
                new MergedBasketBuilder.TailEntry("BOGUS", "NotInUniverse", 50m, "Unknown"),
            },
            SourceName: "alphavantage",
            IsProxy: false,
            AsOfDate: AsOf);

        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AAPL", "MSFT",
        };

        var result = MergedBasketBuilder.BuildAnchored(
            anchor, tail, universe,
            basketId: "HQQQ", version: "v-test");

        // Anchor always keeps AAPL; tail keeps MSFT only; BOGUS dropped.
        Assert.Equal(2, result.Quality.FinalSymbolCount);
        Assert.Equal(1, result.Quality.UniverseDroppedCount);
        Assert.Contains(result.Snapshot.Constituents, c => c.Symbol == "MSFT");
        Assert.DoesNotContain(result.Snapshot.Constituents, c => c.Symbol == "BOGUS");
    }

    [Fact]
    public void Build_AnchorLessProxy_MarksDegradedAndNoOfficialShares()
    {
        // Explicit fallback when no anchor is available.
        var tail = new MergedBasketBuilder.TailBlock(
            new[] { new MergedBasketBuilder.TailEntry("AAPL", "Apple", 100m, "Technology") },
            SourceName: "nasdaq",
            IsProxy: true,
            AsOfDate: AsOf);

        var result = MergedBasketBuilder.Build(tail, null, "HQQQ", "v-test");

        Assert.True(result.Quality.IsDegraded);
        Assert.False(result.Quality.HasOfficialShares);
        Assert.Equal("anchor-less-proxy", result.Quality.BasketMode);
        Assert.Null(result.Quality.AnchorSource);
        Assert.Equal("live:nasdaq:proxy", result.Snapshot.Source);
        Assert.All(result.Snapshot.Constituents, c => Assert.Equal(0m, c.SharesHeld));
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
