using Hqqq.Domain.Entities;

namespace Hqqq.Domain.Services;

/// <summary>
/// Pure domain calculation for the un-scaled basket value
/// (sum of shares × latest price across priced constituents).
/// Migrated from the legacy <c>Hqqq.Api.Modules.Pricing.Services.PricingEngine.ComputeRawValue</c>.
/// </summary>
public static class BasketRawValueCalculator
{
    public static decimal Compute(
        IReadOnlyList<PricingBasisEntry> entries,
        IReadOnlyDictionary<string, decimal> prices)
    {
        decimal total = 0m;
        foreach (var e in entries)
        {
            if (prices.TryGetValue(e.Symbol, out var p) && p > 0m)
                total += p * e.Shares;
        }
        return total;
    }
}
