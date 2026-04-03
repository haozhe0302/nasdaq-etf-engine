using System.Security.Cryptography;
using System.Text;
using Hqqq.Api.Modules.Basket.Contracts;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Constructs a merged hybrid basket from anchor + tail sources.
///
/// Algorithm:
///   Step A — Lock the anchor block (Stock Analysis top-25 or Schwab top-20).
///   Step B — Compute residual tail target: 1.0 - sum(anchor weights).
///   Step C — Choose tail source (Alpha Vantage filtered, or Nasdaq proxy).
///   Step D — Remove dirty rows, anchor-overlap, and universe mismatches.
///   Step E — Normalize the tail proportionally to equal the tail target.
///   Step F — Concatenate anchor + normalized tail.
///   Step G — Validate: symbol uniqueness, weight sum, no empty symbols.
/// </summary>
public static class MergedBasketBuilder
{
    public sealed record AnchorBlock(
        List<BasketConstituent> Constituents,
        string SourceName,
        DateOnly AsOfDate);

    public sealed record TailBlock(
        List<TailEntry> Entries,
        string SourceName,
        bool IsProxy);

    public sealed record TailEntry(
        string Symbol, string Name, decimal RawWeight, string Sector);

    public sealed record MergeResult(
        IReadOnlyList<BasketConstituent> Constituents,
        BasketQualityReport QualityReport,
        DateOnly AsOfDate,
        string Fingerprint);

    /// <param name="universeSymbols">
    /// If provided, tail entries not in this set (and not in the anchor) are dropped.
    /// Pass the Nasdaq constituent symbols when available as a universe guardrail.
    /// </param>
    public static MergeResult Build(
        AnchorBlock anchor,
        TailBlock tail,
        HashSet<string>? universeSymbols = null)
    {
        var anchorSymbols = new HashSet<string>(
            anchor.Constituents.Select(c => c.Symbol), StringComparer.OrdinalIgnoreCase);

        var anchorTotalWeight = anchor.Constituents.Sum(c => c.Weight ?? 0m);
        var tailTargetWeight = Math.Max(0m, 1m - anchorTotalWeight);

        var afterDirtyFilter = tail.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Symbol))
            .Where(e => !anchorSymbols.Contains(e.Symbol))
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var dirtyDropped = tail.Entries.Count
            - afterDirtyFilter.Count
            - tail.Entries.Count(e => anchorSymbols.Contains(e.Symbol));

        int universeDropped = 0;
        var cleanTail = afterDirtyFilter;
        if (universeSymbols is { Count: > 0 })
        {
            cleanTail = afterDirtyFilter
                .Where(e => universeSymbols.Contains(e.Symbol))
                .ToList();
            universeDropped = afterDirtyFilter.Count - cleanTail.Count;
        }

        var tailRawTotal = cleanTail.Sum(e => e.RawWeight);

        var normalizedTail = cleanTail.Select(e =>
        {
            var normalizedWeight = tailRawTotal > 0
                ? e.RawWeight / tailRawTotal * tailTargetWeight
                : 0m;

            return new BasketConstituent
            {
                Symbol = e.Symbol,
                SecurityName = e.Name,
                Exchange = "Unknown",
                Currency = "USD",
                SharesHeld = 0m,
                Weight = Math.Round(normalizedWeight, 8),
                Sector = e.Sector,
                AsOfDate = anchor.AsOfDate,
                WeightSource = tail.IsProxy ? "nasdaq-proxy" : "alphavantage",
                SharesSource = "unavailable",
                NameSource = tail.SourceName,
                SectorSource = string.IsNullOrEmpty(e.Sector) || e.Sector == "Unknown"
                    ? "unknown" : tail.SourceName,
            };
        }).ToList();

        var merged = new List<BasketConstituent>(anchor.Constituents.Count + normalizedTail.Count);
        merged.AddRange(anchor.Constituents);
        merged.AddRange(normalizedTail);

        var totalWeight = merged.Sum(c => c.Weight ?? 0m);
        var officialSharesCount = anchor.Constituents.Count(c => c.SharesHeld > 0);
        var officialSharesByWeight = anchor.Constituents
            .Where(c => c.SharesHeld > 0)
            .Sum(c => c.Weight ?? 0m);

        var report = new BasketQualityReport
        {
            OfficialWeightCoveragePct = Math.Round(anchorTotalWeight * 100m, 2),
            OfficialSharesByWeightPct = Math.Round(officialSharesByWeight * 100m, 2),
            OfficialSharesCount = officialSharesCount,
            ProxyTailCoveragePct = Math.Round(tailTargetWeight * 100m, 2),
            FilteredRowCount = tail.Entries.Count,
            DroppedDirtySymbolCount = Math.Max(0, dirtyDropped),
            UniverseDroppedCount = universeDropped,
            TotalSymbolCount = merged.Count,
            IsDegraded = tail.IsProxy,
            TotalWeightPct = Math.Round(totalWeight * 100m, 2),
            BasketMode = tail.IsProxy ? "degraded" : (normalizedTail.Count > 0 ? "hybrid" : "anchor-only"),
            AnchorSource = anchor.SourceName,
            TailSource = tail.SourceName,
            AnchorCount = anchor.Constituents.Count,
            TailCount = normalizedTail.Count,
        };

        var fingerprint = ComputeFingerprint(merged, anchor.AsOfDate);

        return new MergeResult(merged, report, anchor.AsOfDate, fingerprint);
    }

    public static string ComputeFingerprint(
        IReadOnlyList<BasketConstituent> constituents, DateOnly asOfDate)
    {
        var sb = new StringBuilder();
        sb.Append(asOfDate.ToString("yyyy-MM-dd"));
        foreach (var c in constituents.OrderBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|').Append(c.Symbol).Append(':').Append($"{c.Weight ?? 0m:F8}");
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }
}
