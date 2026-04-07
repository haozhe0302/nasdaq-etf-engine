using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.CorporateActions.Contracts;
using Hqqq.Api.Modules.CorporateActions.Services;
using Hqqq.Api.Modules.Pricing.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Api.Tests.CorporateActions;

public class CorporateActionAdjustmentServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static readonly DateOnly BasketDate = new(2026, 3, 27);
    private static readonly DateOnly RuntimeDate = new(2026, 4, 3);

    private static BasketSnapshot MakeBasket(
        DateOnly? asOfDate = null, params BasketConstituent[] constituents) =>
        new()
        {
            AsOfDate = asOfDate ?? BasketDate,
            Constituents = constituents.ToList(),
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Source = new BasketSourceInfo
            {
                SourceName = "test",
                SourceType = "test",
                IsDegraded = false,
                SourceAsOfDate = asOfDate ?? BasketDate,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                CacheWrittenAtUtc = DateTimeOffset.UtcNow,
                OfficialWeightsAvailable = true,
                OfficialSharesAvailable = true,
            },
            Fingerprint = "test-fp-001",
        };

    private static BasketConstituent OfficialConstituent(
        string symbol, decimal weight, decimal shares) =>
        new()
        {
            Symbol = symbol,
            SecurityName = $"{symbol} Inc.",
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = weight,
            SharesHeld = shares,
            AsOfDate = BasketDate,
            SharesSource = "stockanalysis",
        };

    private static BasketConstituent DerivedConstituent(
        string symbol, decimal weight) =>
        new()
        {
            Symbol = symbol,
            SecurityName = $"{symbol} Corp.",
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = weight,
            SharesHeld = 0m,
            AsOfDate = BasketDate,
            SharesSource = "unavailable",
        };

    private static CorporateActionAdjustmentService CreateService(
        ICorporateActionProvider provider) =>
        new(provider, NullLogger<CorporateActionAdjustmentService>.Instance);

    // ── Stub provider ───────────────────────────────────────────

    private sealed class StubProvider : ICorporateActionProvider
    {
        private readonly List<SplitEvent> _splits;
        public bool ShouldFail { get; set; }
        public string FailureMessage { get; set; } = "Provider unavailable";

        public StubProvider(params SplitEvent[] splits)
        {
            _splits = splits.ToList();
        }

        public Task<IReadOnlyList<SplitEvent>> GetSplitsAsync(
            IEnumerable<string> symbols, DateOnly fromDate, DateOnly toDate,
            CancellationToken ct = default)
        {
            if (ShouldFail)
                throw new HttpRequestException(FailureMessage);

            var symbolSet = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<SplitEvent> result = _splits
                .Where(s => symbolSet.Contains(s.Symbol))
                .Where(s => s.EffectiveDate >= fromDate && s.EffectiveDate <= toDate)
                .ToList();
            return Task.FromResult(result);
        }
    }

    private static SplitEvent MakeSplit(
        string symbol, DateOnly date, decimal factor) =>
        new()
        {
            Symbol = symbol,
            EffectiveDate = date,
            Factor = factor,
            Source = "test",
            Description = $"{factor}:1 split",
        };

    // ── 1. No corporate actions → identical pricing inputs ──────

    [Fact]
    public async Task NoSplits_ReturnsOriginalSharesUnchanged()
    {
        var provider = new StubProvider();
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 45000),
            OfficialConstituent("MSFT", 0.25m, 30000),
            DerivedConstituent("GOOG", 0.10m));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
        Assert.Equal(3, result.Report.UnadjustedConstituentCount);
        Assert.Empty(result.Report.Adjustments);
        Assert.False(result.Report.ProviderFailed);

        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(45000m, aapl.SharesHeld);
        Assert.Equal("stockanalysis", aapl.SharesSource);
    }

    // ── 2. Single forward split ─────────────────────────────────

    [Fact]
    public async Task SingleForwardSplit_AdjustsSharesAndSetsProvenance()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(2), 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000),
            OfficialConstituent("MSFT", 0.25m, 30000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(1, result.Report.AdjustedConstituentCount);
        Assert.Equal(1, result.Report.UnadjustedConstituentCount);

        var adj = result.Report.Adjustments.Single();
        Assert.Equal("AAPL", adj.Symbol);
        Assert.Equal(10000m, adj.OriginalShares);
        Assert.Equal(40000m, adj.AdjustedShares);
        Assert.Equal(4m, adj.CumulativeSplitFactor);

        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(40000m, aapl.SharesHeld);
        Assert.Contains("split-adjusted", aapl.SharesSource);

        var msft = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "MSFT");
        Assert.Equal(30000m, msft.SharesHeld);
        Assert.DoesNotContain("split-adjusted", msft.SharesSource);
    }

    // ── 3. Multiple splits across the basket lag window ─────────

    [Fact]
    public async Task MultipleSplits_CumulativeFactorApplied()
    {
        var split1 = MakeSplit("AAPL", BasketDate.AddDays(1), 2m);
        var split2 = MakeSplit("AAPL", BasketDate.AddDays(3), 3m);
        var provider = new StubProvider(split1, split2);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        var adj = result.Report.Adjustments.Single();
        Assert.Equal(6m, adj.CumulativeSplitFactor);
        Assert.Equal(60000m, adj.AdjustedShares);
        Assert.Equal(2, adj.AppliedSplits.Count);
    }

    [Fact]
    public async Task MultipleSplits_DifferentSymbols()
    {
        var splitA = MakeSplit("AAPL", BasketDate.AddDays(1), 4m);
        var splitM = MakeSplit("MSFT", BasketDate.AddDays(2), 2m);
        var provider = new StubProvider(splitA, splitM);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000),
            OfficialConstituent("MSFT", 0.25m, 20000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(2, result.Report.AdjustedConstituentCount);

        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(40000m, aapl.SharesHeld);

        var msft = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "MSFT");
        Assert.Equal(40000m, msft.SharesHeld);
    }

    // ── 4. Split before basket as-of date → no adjustment ───────

    [Fact]
    public async Task SplitBeforeBasketDate_NoAdjustment()
    {
        var oldSplit = MakeSplit("AAPL", BasketDate.AddDays(-5), 4m);
        var provider = new StubProvider(oldSplit);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(10000m, aapl.SharesHeld);
    }

    [Fact]
    public async Task SplitOnBasketDate_NoAdjustment()
    {
        var sameDaySplit = MakeSplit("AAPL", BasketDate, 4m);
        var provider = new StubProvider(sameDaySplit);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(10000m, aapl.SharesHeld);
    }

    // ── 5. Provider failure → graceful fallback ─────────────────

    [Fact]
    public async Task ProviderFailure_ReturnsOriginalSharesWithFlag()
    {
        var provider = new StubProvider { ShouldFail = true };
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.True(result.Report.ProviderFailed);
        Assert.NotNull(result.Report.ProviderError);
        Assert.Equal(0, result.Report.AdjustedConstituentCount);

        var aapl = result.AdjustedSnapshot.Constituents.Single(c => c.Symbol == "AAPL");
        Assert.Equal(10000m, aapl.SharesHeld);
        Assert.Equal("stockanalysis", aapl.SharesSource);
    }

    // ── 6. Immutability of original basket snapshot ─────────────

    [Fact]
    public async Task OriginalSnapshotIsNeverMutated()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(1), 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000),
            OfficialConstituent("MSFT", 0.25m, 20000));

        var originalAaplShares = basket.Constituents
            .Single(c => c.Symbol == "AAPL").SharesHeld;
        var originalFingerprint = basket.Fingerprint;
        var originalCount = basket.Constituents.Count;

        var result = await service.AdjustAsync(basket);

        Assert.Equal(originalAaplShares, basket.Constituents
            .Single(c => c.Symbol == "AAPL").SharesHeld);
        Assert.Equal(originalFingerprint, basket.Fingerprint);
        Assert.Equal(originalCount, basket.Constituents.Count);
        Assert.Equal("stockanalysis", basket.Constituents
            .Single(c => c.Symbol == "AAPL").SharesSource);

        Assert.Equal(originalFingerprint, result.AdjustedSnapshot.Fingerprint);
    }

    // ── 7. Pricing basis provenance after adjustment ────────────

    [Fact]
    public async Task PricingBasisShowsSplitAdjustedOrigin()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(1), 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000),
            OfficialConstituent("MSFT", 0.25m, 30000));

        var result = await service.AdjustAsync(basket);

        var builder = new BasketPricingBasisBuilder();
        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 50m,
            ["MSFT"] = 400m,
        };

        var basis = builder.Build(result.AdjustedSnapshot, prices);

        var aaplEntry = basis.Entries.Single(e => e.Symbol == "AAPL");
        Assert.Equal("official:split-adjusted", aaplEntry.SharesOrigin);
        Assert.Equal(40000, aaplEntry.Shares);

        var msftEntry = basis.Entries.Single(e => e.Symbol == "MSFT");
        Assert.Equal("official", msftEntry.SharesOrigin);
        Assert.Equal(30000, msftEntry.Shares);
    }

    // ── 8. Reverse split ────────────────────────────────────────

    [Fact]
    public async Task ReverseSplit_ReducesShares()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(1), 0.25m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 40000));

        var result = await service.AdjustAsync(basket);

        var adj = result.Report.Adjustments.Single();
        Assert.Equal(0.25m, adj.CumulativeSplitFactor);
        Assert.Equal(10000m, adj.AdjustedShares);
    }

    // ── 9. Derived-only constituents are not queried for splits ──

    [Fact]
    public async Task DerivedConstituents_AreNotAdjusted()
    {
        var split = MakeSplit("GOOG", BasketDate.AddDays(1), 20m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            DerivedConstituent("GOOG", 0.10m));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
        var goog = result.AdjustedSnapshot.Constituents.Single();
        Assert.Equal(0m, goog.SharesHeld);
    }

    // ── 10. Runtime date == basket date → no adjustment ─────────

    [Fact]
    public async Task RuntimeDateSameAsBasketDate_NoAdjustment()
    {
        var split = MakeSplit("AAPL", BasketDate, 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var basket = MakeBasket(today,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
    }

    // ── 11. LastReport is exposed for diagnostics ───────────────

    [Fact]
    public async Task LastReport_ExposedAfterAdjust()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(1), 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);

        Assert.Null(service.LastReport);

        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));
        await service.AdjustAsync(basket);

        var report = service.LastReport;
        Assert.NotNull(report);
        Assert.Equal(1, report.AdjustedConstituentCount);
        Assert.Equal(BasketDate, report.BasketAsOfDate);
    }

    // ── 12. Caching: same fingerprint + date → cached result ────

    [Fact]
    public async Task CachedResult_ReturnedForSameBasketAndDate()
    {
        var callCount = 0;
        var countingProvider = new CountingProvider(() => callCount++);
        var service = CreateService(countingProvider);

        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var r1 = await service.AdjustAsync(basket);
        var r2 = await service.AdjustAsync(basket);

        Assert.Same(r1, r2);
        Assert.Equal(1, callCount);
    }

    private sealed class CountingProvider : ICorporateActionProvider
    {
        private readonly Action _onCall;
        public CountingProvider(Action onCall) => _onCall = onCall;

        public Task<IReadOnlyList<SplitEvent>> GetSplitsAsync(
            IEnumerable<string> symbols, DateOnly fromDate, DateOnly toDate,
            CancellationToken ct = default)
        {
            _onCall();
            return Task.FromResult<IReadOnlyList<SplitEvent>>([]);
        }
    }

    // ── 13. Report dates are correct ────────────────────────────

    [Fact]
    public async Task Report_ContainsCorrectDates()
    {
        var provider = new StubProvider();
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(BasketDate, result.Report.BasketAsOfDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), result.Report.RuntimeDate);
        Assert.True(result.Report.ComputedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    // ── 14. Fingerprint preserved on adjusted snapshot ───────────

    [Fact]
    public async Task AdjustedSnapshot_PreservesOriginalFingerprint()
    {
        var split = MakeSplit("AAPL", BasketDate.AddDays(1), 4m);
        var provider = new StubProvider(split);
        var service = CreateService(provider);
        var basket = MakeBasket(null,
            OfficialConstituent("AAPL", 0.30m, 10000));

        var result = await service.AdjustAsync(basket);

        Assert.Equal(basket.Fingerprint, result.AdjustedSnapshot.Fingerprint);
        Assert.Equal(basket.AsOfDate, result.AdjustedSnapshot.AsOfDate);
        Assert.Equal(basket.Source, result.AdjustedSnapshot.Source);
    }

    // ── 15. Empty basket → no-op ────────────────────────────────

    [Fact]
    public async Task EmptyBasket_ReturnsNoAdjustment()
    {
        var provider = new StubProvider();
        var service = CreateService(provider);
        var basket = MakeBasket(null);

        var result = await service.AdjustAsync(basket);

        Assert.Equal(0, result.Report.AdjustedConstituentCount);
        Assert.Equal(0, result.Report.UnadjustedConstituentCount);
        Assert.Empty(result.AdjustedSnapshot.Constituents);
    }
}
