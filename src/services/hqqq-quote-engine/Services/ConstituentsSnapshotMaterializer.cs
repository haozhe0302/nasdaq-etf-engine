using Hqqq.Contracts.Dtos;
using Hqqq.Domain.Services;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Builds a <see cref="ConstituentsSnapshotDto"/> from the current
/// <see cref="BasketStateStore"/> + <see cref="PerSymbolQuoteStore"/>. Shape
/// matches the frontend <c>BConstituentSnapshot</c> adapter so the gateway
/// reader in B5 can serve the cached JSON payload without remapping.
/// </summary>
/// <remarks>
/// Full provenance fields on <see cref="BasketSourceDto"/> (anchor/tail
/// source, degraded flag) are filled with conservative defaults here; those
/// will be wired from reference-data in a later step.
/// </remarks>
public sealed class ConstituentsSnapshotMaterializer
{
    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly ISystemClock _clock;
    private readonly QuoteEngineOptions _options;

    public ConstituentsSnapshotMaterializer(
        PerSymbolQuoteStore quotes,
        BasketStateStore baskets,
        ISystemClock clock,
        QuoteEngineOptions options)
    {
        _quotes = quotes;
        _baskets = baskets;
        _clock = clock;
        _options = options;
    }

    public ConstituentsSnapshotDto? Build()
    {
        var basket = _baskets.Current;
        if (basket is null) return null;

        var now = _clock.UtcNow;
        var holdings = BuildHoldings(basket, now);
        var concentration = BuildConcentration(basket);
        var quality = BuildQuality(basket, holdings);
        var source = BuildSource(basket);

        return new ConstituentsSnapshotDto
        {
            Holdings = holdings,
            Concentration = concentration,
            Quality = quality,
            Source = source,
            AsOf = now,
        };
    }

    private IReadOnlyList<ConstituentHoldingDto> BuildHoldings(ActiveBasket basket, DateTimeOffset now)
    {
        var entries = basket.PricingBasis.Entries;
        var constituents = basket.Constituents;

        var entryBySymbol = new Dictionary<string, Hqqq.Domain.Entities.PricingBasisEntry>(
            capacity: entries.Count, comparer: StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            entryBySymbol[e.Symbol] = e;

        var results = new List<ConstituentHoldingDto>(constituents.Count);
        foreach (var c in constituents)
        {
            entryBySymbol.TryGetValue(c.Symbol, out var entry);
            var state = _quotes.Get(c.Symbol);

            decimal? price = state is { Price: > 0m } ? state.Price : null;
            decimal? changePct = null;
            if (price is > 0m)
            {
                var prev = state!.PreviousClose;
                if (prev is > 0m)
                    changePct = Math.Round((price.Value - prev.Value) / prev.Value * 100m, 4);
            }

            decimal? marketValue = price is > 0m ? price.Value * c.SharesHeld : null;
            var weightPct = entry is { TargetWeight: { } w }
                ? Math.Round(w * 100m, 4)
                : (c.TargetWeight is { } cw ? Math.Round(cw * 100m, 4) : 0m);

            var isStale = state is null
                || FreshnessSummarizer.IsStale(state.ReceivedAtUtc, now, _options.StaleAfter);

            results.Add(new ConstituentHoldingDto
            {
                Symbol = c.Symbol,
                Name = c.SecurityName,
                Sector = c.Sector,
                Weight = weightPct,
                Shares = c.SharesHeld,
                Price = price,
                ChangePct = changePct,
                MarketValue = marketValue is null ? null : Math.Round(marketValue.Value, 2),
                SharesOrigin = c.SharesOrigin,
                IsStale = isStale,
            });
        }

        return results;
    }

    private static ConcentrationDto BuildConcentration(ActiveBasket basket)
    {
        var weights = basket.Constituents
            .Select(c => c.TargetWeight ?? 0m)
            .OrderByDescending(w => w)
            .ToList();

        decimal TopN(int n) =>
            Math.Round(weights.Take(n).Sum() * 100m, 4);

        var hhi = Math.Round(weights.Sum(w => w * w), 6);
        var sectorCount = basket.Constituents
            .Where(c => !string.IsNullOrWhiteSpace(c.Sector))
            .Select(c => c.Sector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new ConcentrationDto
        {
            Top5Pct = TopN(5),
            Top10Pct = TopN(10),
            Top20Pct = TopN(20),
            SectorCount = sectorCount,
            HerfindahlIndex = hhi,
        };
    }

    private BasketQualityDto BuildQuality(
        ActiveBasket basket,
        IReadOnlyList<ConstituentHoldingDto> holdings)
    {
        var total = holdings.Count;
        var priced = holdings.Count(h => h.Price is > 0m);
        var stale = holdings.Count(h => h.IsStale);
        var coverage = total > 0
            ? Math.Round((decimal)priced / total * 100m, 1)
            : 0m;

        var basketMode = basket.PricingBasis.DerivedSharesCount > 0 ? "hybrid" : "official";

        return new BasketQualityDto
        {
            TotalSymbols = total,
            OfficialSharesCount = basket.PricingBasis.OfficialSharesCount,
            DerivedSharesCount = basket.PricingBasis.DerivedSharesCount,
            PricedCount = priced,
            StaleCount = stale,
            PriceCoveragePct = coverage,
            BasketMode = basketMode,
        };
    }

    private static BasketSourceDto BuildSource(ActiveBasket basket)
    {
        var basketMode = basket.PricingBasis.DerivedSharesCount > 0 ? "hybrid" : "official";
        return new BasketSourceDto
        {
            AnchorSource = string.Empty,
            TailSource = string.Empty,
            BasketMode = basketMode,
            IsDegraded = false,
            AsOfDate = basket.AsOfDate.ToString("yyyy-MM-dd"),
            Fingerprint = basket.Fingerprint,
        };
    }
}
