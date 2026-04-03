using System.Security.Cryptography;
using System.Text;
using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.Pricing.Contracts;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Builds a consistent quantity vector (pricing basis) from a hybrid basket snapshot.
/// <list type="bullet">
///   <item>Rows with official disclosed shares → used directly</item>
///   <item>Rows with only a target weight → shares derived from inferred total notional</item>
/// </list>
/// The inferred total notional is extrapolated from the anchor block's market value
/// and its weight coverage. If no official shares exist at all, a default synthetic
/// notional of $100 B is used.
/// </summary>
public sealed class BasketPricingBasisBuilder
{
    private const decimal DefaultSyntheticNotional = 100_000_000_000m;

    public PricingBasis Build(
        BasketSnapshot basket,
        IReadOnlyDictionary<string, decimal> referencePrices)
    {
        var officialEntries = new List<PricingBasisEntry>();

        decimal anchorMarketValue = 0m;
        decimal anchorWeightCoverage = 0m;

        foreach (var c in basket.Constituents)
        {
            if (!referencePrices.TryGetValue(c.Symbol, out var price) || price <= 0)
                continue;

            bool hasOfficialShares = c.SharesHeld > 0
                && !c.SharesSource.Contains("derived", StringComparison.OrdinalIgnoreCase);

            if (!hasOfficialShares) continue;

            var shares = (int)Math.Max(1, Math.Round((double)c.SharesHeld));
            anchorMarketValue += price * shares;
            anchorWeightCoverage += c.Weight ?? 0m;

            officialEntries.Add(new PricingBasisEntry
            {
                Symbol = c.Symbol,
                Shares = shares,
                ReferencePrice = price,
                SharesOrigin = "official",
                TargetWeight = c.Weight,
            });
        }

        decimal inferredNotional = anchorWeightCoverage > 0.01m
            ? anchorMarketValue / anchorWeightCoverage
            : DefaultSyntheticNotional;

        var derivedEntries = new List<PricingBasisEntry>();
        foreach (var c in basket.Constituents)
        {
            if (!referencePrices.TryGetValue(c.Symbol, out var price) || price <= 0)
                continue;

            bool hasOfficialShares = c.SharesHeld > 0
                && !c.SharesSource.Contains("derived", StringComparison.OrdinalIgnoreCase);
            if (hasOfficialShares) continue;

            var weight = c.Weight ?? 0m;
            if (weight <= 0) continue;

            var derivedShares = Math.Max(1,
                (int)Math.Round((double)(weight * inferredNotional / price)));

            derivedEntries.Add(new PricingBasisEntry
            {
                Symbol = c.Symbol,
                Shares = derivedShares,
                ReferencePrice = price,
                SharesOrigin = "derived",
                TargetWeight = weight,
            });
        }

        var allEntries = officialEntries.Concat(derivedEntries).ToList();
        var fingerprint = ComputeFingerprint(allEntries);

        return new PricingBasis
        {
            BasketFingerprint = basket.Fingerprint,
            PricingBasisFingerprint = fingerprint,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Entries = allEntries,
            InferredTotalNotional = inferredNotional,
            OfficialSharesCount = officialEntries.Count,
            DerivedSharesCount = derivedEntries.Count,
        };
    }

    private static string ComputeFingerprint(List<PricingBasisEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries.OrderBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase))
            sb.Append(e.Symbol).Append(':').Append(e.Shares).Append(';');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }
}
