using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/MergedBasketBuilder.cs</c>.
/// Constructs a merged basket snapshot from an anchor block + a tail
/// block, optionally filtered by a universe guardrail.
/// </summary>
/// <remarks>
/// <para>
/// The standard Production merge is:
/// <list type="number">
///   <item>Anchor block = freshest of StockAnalysis or Schwab (tie → StockAnalysis). Anchor rows carry real <c>SharesHeld</c>.</item>
///   <item>Tail block = AlphaVantage weights (preferred) or Nasdaq proxy weights (degraded).</item>
///   <item>Universe guardrail = Nasdaq symbols set.</item>
///   <item>Lock anchor weights; <c>tailTargetWeight = 1 - Σanchor.RawWeight</c>; dedupe tail, drop anchor overlap, apply universe, normalize tail raw weights to <c>tailTargetWeight</c>.</item>
///   <item>Emit a <see cref="HoldingsSnapshot"/> with combined constituents, lineage per row, and a 16-char content fingerprint.</item>
/// </list>
/// When no anchor is available the anchor-less <see cref="Build(TailBlock, HashSet{string}?, string, string)"/>
/// path runs with <c>SharesHeld = 0</c> across all rows and
/// <c>IsDegraded = true</c>; <see cref="RealSourceBasketPipeline"/>
/// only takes that path when the operator has explicitly opted in via
/// <c>AllowAnchorlessProxyInProduction</c>.
/// </para>
/// <para>
/// The 16-character fingerprint is kept identical to Phase 1 so an
/// operator can correlate fingerprints across log streams during the
/// migration. Phase 2's publish fingerprint
/// (<see cref="HoldingsFingerprint"/>) is the authoritative state-
/// machine key; this fingerprint is for human/log traceability only.
/// </para>
/// </remarks>
public static class MergedBasketBuilder
{
    public sealed record AnchorBlock(
        IReadOnlyList<AnchorEntry> Entries,
        string SourceName,
        DateOnly AsOfDate);

    public sealed record AnchorEntry(
        string Symbol,
        string Name,
        string Sector,
        decimal SharesHeld,
        decimal RawWeight);

    public sealed record TailBlock(
        IReadOnlyList<TailEntry> Entries,
        string SourceName,
        bool IsProxy,
        DateOnly AsOfDate);

    public sealed record TailEntry(string Symbol, string Name, decimal RawWeight, string Sector);

    public sealed record MergeResult(
        HoldingsSnapshot Snapshot,
        MergeQualityReport Quality);

    public sealed record MergeQualityReport
    {
        public required int InputRowCount { get; init; }
        public required int DroppedDirtyCount { get; init; }
        public required int UniverseDroppedCount { get; init; }
        public required int FinalSymbolCount { get; init; }
        public required decimal TotalWeight { get; init; }
        public required string BasketMode { get; init; }
        public required string TailSource { get; init; }
        public required bool IsDegraded { get; init; }
        public required string ContentFingerprint16 { get; init; }
        public string? AnchorSource { get; init; }
        public bool HasOfficialShares { get; init; }
        public int AnchorRowCount { get; init; }
        public int TailRowCount { get; init; }
    }

    /// <summary>
    /// Standard Production merge: anchor + tail + optional universe
    /// guardrail. Anchor rows preserve authoritative <see cref="HoldingsConstituent.SharesHeld"/>
    /// from the scraper; tail rows carry <c>SharesHeld = 0m</c> with
    /// <c>SharesSource = "unavailable"</c>.
    /// </summary>
    public static MergeResult BuildAnchored(
        AnchorBlock anchor,
        TailBlock tail,
        HashSet<string>? universeSymbols,
        string basketId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(tail);

        // ── Anchor normalization: dedupe, keep raw weights as-is. ─────
        var anchorEntries = anchor.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Symbol))
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var anchorSymbols = anchorEntries
            .Select(e => e.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Anchor weights are reported as % in Phase 1 (e.g. 9.25 means
        // 9.25%). Convert to fraction space so the sum lives in [0..1].
        var anchorFractions = anchorEntries
            .Select(e => e.RawWeight / 100m)
            .ToList();
        var anchorWeightSum = anchorFractions.Sum();

        // Clamp so a noisy scraper can't push total > 1.0.
        var tailTargetWeight = Math.Max(0m, 1m - anchorWeightSum);

        // ── Tail normalization: dedupe, drop anchor overlap, universe. ─
        var tailAfterDedup = tail.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Symbol))
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(e => !anchorSymbols.Contains(e.Symbol))
            .ToList();

        var dirtyDropped = tail.Entries.Count - tailAfterDedup.Count;

        var cleanTail = tailAfterDedup;
        var universeDropped = 0;
        if (universeSymbols is { Count: > 0 })
        {
            cleanTail = tailAfterDedup
                .Where(e => universeSymbols.Contains(e.Symbol))
                .ToList();
            universeDropped = tailAfterDedup.Count - cleanTail.Count;
        }

        // Tail raw weights are also % from AlphaVantage; for Nasdaq-proxy
        // they arrive as % too (market-cap share * 100). Normalize within
        // the tail block and scale so the total tail weight equals
        // tailTargetWeight.
        var tailRawTotal = cleanTail.Sum(e => e.RawWeight);
        var tailConstituents = cleanTail.Select(e =>
        {
            var normalized = tailRawTotal > 0m && tailTargetWeight > 0m
                ? e.RawWeight / tailRawTotal * tailTargetWeight
                : 0m;

            var sector = string.IsNullOrWhiteSpace(e.Sector) ? "Unknown" : e.Sector;
            var sectorSource = (string.IsNullOrEmpty(e.Sector) || e.Sector == "Unknown")
                ? "unknown" : tail.SourceName;

            return new HoldingsConstituent
            {
                Symbol = e.Symbol,
                Name = string.IsNullOrWhiteSpace(e.Name) ? "Unknown" : e.Name,
                Sector = sector,
                SharesHeld = 0m,
                ReferencePrice = 0m,
                TargetWeight = Math.Round(normalized, 8),
                WeightSource = tail.IsProxy ? "nasdaq-proxy" : tail.SourceName,
                SharesSource = "unavailable",
                NameSource = tail.SourceName,
                SectorSource = sectorSource,
            };
        }).ToList();

        // ── Anchor projection: preserve shares + convert % to fraction. ─
        var anchorConstituents = anchorEntries.Select((e, i) =>
        {
            var fraction = anchorWeightSum > 0m
                ? anchorFractions[i]
                : 0m;

            var sector = string.IsNullOrWhiteSpace(e.Sector) ? "Unknown" : e.Sector;
            var sectorSource = (string.IsNullOrEmpty(e.Sector) || e.Sector == "Unknown")
                ? "unknown" : anchor.SourceName;

            return new HoldingsConstituent
            {
                Symbol = e.Symbol,
                Name = string.IsNullOrWhiteSpace(e.Name) ? "Unknown" : e.Name,
                Sector = sector,
                SharesHeld = e.SharesHeld,
                ReferencePrice = 0m,
                TargetWeight = Math.Round(fraction, 8),
                WeightSource = anchor.SourceName,
                SharesSource = e.SharesHeld > 0m ? anchor.SourceName : "unavailable",
                NameSource = anchor.SourceName,
                SectorSource = sectorSource,
            };
        }).ToList();

        var allConstituents = anchorConstituents
            .Concat(tailConstituents)
            .ToList();

        var fingerprint16 = ComputeContentFingerprint16(allConstituents, anchor.AsOfDate);
        var hasOfficialShares = anchorConstituents.Any(c => c.SharesHeld > 0m);
        var source = $"live:{anchor.SourceName}+{tail.SourceName}" +
                     (tail.IsProxy ? ":proxy" : string.Empty);

        var snapshot = new HoldingsSnapshot
        {
            BasketId = basketId,
            Version = version,
            AsOfDate = anchor.AsOfDate,
            ScaleFactor = 1m,
            Constituents = allConstituents,
            Source = source,
        };

        var basketMode = tail.IsProxy ? "anchored-proxy-tail" : "anchored";
        var report = new MergeQualityReport
        {
            InputRowCount = anchor.Entries.Count + tail.Entries.Count,
            DroppedDirtyCount = Math.Max(0, dirtyDropped),
            UniverseDroppedCount = universeDropped,
            FinalSymbolCount = allConstituents.Count,
            TotalWeight = Math.Round(allConstituents.Sum(c => c.TargetWeight ?? 0m), 8),
            BasketMode = basketMode,
            TailSource = tail.SourceName,
            IsDegraded = tail.IsProxy,
            ContentFingerprint16 = fingerprint16,
            AnchorSource = anchor.SourceName,
            HasOfficialShares = hasOfficialShares,
            AnchorRowCount = anchorConstituents.Count,
            TailRowCount = tailConstituents.Count,
        };

        return new MergeResult(snapshot, report);
    }

    /// <summary>
    /// Explicit degraded fallback — anchor-less merge. Emits all rows
    /// with <c>SharesHeld = 0m</c> and <c>IsDegraded = true</c>. Only
    /// used when the operator has opted in via
    /// <c>AllowAnchorlessProxyInProduction</c> or outside of Production.
    /// </summary>
    public static MergeResult Build(
        TailBlock tail,
        HashSet<string>? universeSymbols,
        string basketId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(tail);

        var afterDedup = tail.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Symbol))
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var dirtyDropped = tail.Entries.Count - afterDedup.Count;

        var cleanTail = afterDedup;
        var universeDropped = 0;
        if (universeSymbols is { Count: > 0 })
        {
            cleanTail = afterDedup
                .Where(e => universeSymbols.Contains(e.Symbol))
                .ToList();
            universeDropped = afterDedup.Count - cleanTail.Count;
        }

        var rawTotal = cleanTail.Sum(e => e.RawWeight);
        var constituents = cleanTail.Select(e =>
        {
            var normalized = rawTotal > 0m ? e.RawWeight / rawTotal : 0m;
            var sector = string.IsNullOrWhiteSpace(e.Sector) ? "Unknown" : e.Sector;
            var sectorSource = (string.IsNullOrEmpty(e.Sector) || e.Sector == "Unknown")
                ? "unknown" : tail.SourceName;
            return new HoldingsConstituent
            {
                Symbol = e.Symbol,
                Name = string.IsNullOrWhiteSpace(e.Name) ? "Unknown" : e.Name,
                Sector = sector,
                SharesHeld = 0m,
                ReferencePrice = 0m,
                TargetWeight = Math.Round(normalized, 8),
                WeightSource = tail.IsProxy ? "nasdaq-proxy" : tail.SourceName,
                SharesSource = "unavailable",
                NameSource = tail.SourceName,
                SectorSource = sectorSource,
            };
        }).ToList();

        var fingerprint16 = ComputeContentFingerprint16(constituents, tail.AsOfDate);

        var snapshot = new HoldingsSnapshot
        {
            BasketId = basketId,
            Version = version,
            AsOfDate = tail.AsOfDate,
            ScaleFactor = 1m,
            Constituents = constituents,
            Source = tail.IsProxy ? $"live:{tail.SourceName}:proxy" : $"live:{tail.SourceName}",
        };

        var report = new MergeQualityReport
        {
            InputRowCount = tail.Entries.Count,
            DroppedDirtyCount = Math.Max(0, dirtyDropped),
            UniverseDroppedCount = universeDropped,
            FinalSymbolCount = constituents.Count,
            TotalWeight = Math.Round(constituents.Sum(c => c.TargetWeight ?? 0m), 8),
            BasketMode = "anchor-less-proxy",
            TailSource = tail.SourceName,
            IsDegraded = true,
            ContentFingerprint16 = fingerprint16,
            AnchorSource = null,
            HasOfficialShares = false,
            AnchorRowCount = 0,
            TailRowCount = constituents.Count,
        };

        return new MergeResult(snapshot, report);
    }

    public static string ComputeContentFingerprint16(
        IReadOnlyList<HoldingsConstituent> constituents, DateOnly asOfDate)
    {
        var sb = new StringBuilder();
        sb.Append(asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var c in constituents.OrderBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|').Append(c.Symbol).Append(':')
              .Append((c.TargetWeight ?? 0m).ToString("F8", CultureInfo.InvariantCulture));
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }
}
