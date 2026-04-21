using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/MergedBasketBuilder.cs</c>.
/// Constructs a merged basket snapshot from a tail block against an
/// optional universe guardrail. In Phase 2 the anchor block is
/// intentionally disabled — only JSON adapters are ported, so we always
/// build an "anchor-less" AlphaVantage basket (or, if AlphaVantage is
/// absent, a Nasdaq-proxy degraded basket).
/// </summary>
/// <remarks>
/// <para>
/// The merge algorithm is:
/// <list type="number">
///   <item>Drop empty-symbol rows and de-duplicate by symbol.</item>
///   <item>Apply the optional universe guardrail (Nasdaq constituent set).</item>
///   <item>Normalize the remaining raw weights so they sum to 1.0.</item>
///   <item>Emit a <see cref="HoldingsSnapshot"/> with <c>Source</c> lineage and a 16-char content fingerprint.</item>
/// </list>
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
    }

    public static MergeResult Build(
        TailBlock tail,
        HashSet<string>? universeSymbols,
        string basketId,
        string version)
    {
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
            return new HoldingsConstituent
            {
                Symbol = e.Symbol,
                Name = string.IsNullOrWhiteSpace(e.Name) ? "Unknown" : e.Name,
                Sector = string.IsNullOrWhiteSpace(e.Sector) ? "Unknown" : e.Sector,
                SharesHeld = 0m, // JSON adapters do not expose shares — intentional
                ReferencePrice = 0m,
                TargetWeight = Math.Round(normalized, 8),
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
            BasketMode = tail.IsProxy ? "degraded" : "anchor-less",
            TailSource = tail.SourceName,
            IsDegraded = tail.IsProxy,
            ContentFingerprint16 = fingerprint16,
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
