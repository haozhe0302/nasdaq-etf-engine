using Hqqq.Domain.Entities;

namespace Hqqq.Domain.Services;

/// <summary>
/// Pure top-N movers ranked by basis-point impact on the basket.
/// Migrated from the legacy <c>Hqqq.Api.Modules.Pricing.Services.PricingEngine.ComputeMovers</c>;
/// no <c>ILatestPriceStore</c> dependency — all inputs are passed explicitly.
/// </summary>
public static class MoversCalculator
{
    public static IReadOnlyList<MoverResult> Compute(
        IReadOnlyList<PricingBasisEntry> entries,
        IReadOnlyDictionary<string, decimal> latestPrices,
        IReadOnlyDictionary<string, decimal> previousCloses,
        decimal rawValue,
        IReadOnlyDictionary<string, string> names,
        int topN = 5)
    {
        if (rawValue <= 0m || entries.Count == 0) return [];

        var candidates = new List<(string Sym, string Name, decimal Chg, decimal Imp)>(entries.Count);

        foreach (var entry in entries)
        {
            if (!previousCloses.TryGetValue(entry.Symbol, out var prev) || prev <= 0m)
                continue;
            if (!latestPrices.TryGetValue(entry.Symbol, out var price) || price <= 0m)
                continue;

            var chg = (price - prev) / prev * 100m;
            var weight = price * entry.Shares / rawValue;
            var impact = weight * chg * 100m;
            names.TryGetValue(entry.Symbol, out var name);

            candidates.Add((entry.Symbol, name ?? entry.Symbol, chg, impact));
        }

        return candidates
            .OrderByDescending(m => Math.Abs(m.Imp))
            .Take(topN)
            .Select(m => new MoverResult
            {
                Symbol = m.Sym,
                Name = m.Name,
                ChangePct = Math.Round(m.Chg, 2),
                Impact = Math.Round(m.Imp, 2),
                Direction = m.Chg >= 0 ? "up" : "down",
            })
            .ToList();
    }
}
