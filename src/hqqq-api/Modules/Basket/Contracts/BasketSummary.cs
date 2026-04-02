namespace Hqqq.Api.Modules.Basket.Contracts;

public sealed record BasketSummary
{
    public required int ConstituentCount { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required decimal Top5ConcentrationPct { get; init; }
    public required decimal Top10ConcentrationPct { get; init; }
    public required int SectorCount { get; init; }
    public required int MissingSymbolCount { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required BasketSourceInfo Source { get; init; }
    public BasketQualityReport? QualityReport { get; init; }

    public static BasketSummary FromSnapshot(BasketSnapshot snapshot)
    {
        var ordered = snapshot.Constituents
            .OrderByDescending(c => c.Weight ?? 0m)
            .ToList();

        return new BasketSummary
        {
            ConstituentCount = snapshot.Constituents.Count,
            AsOfDate = snapshot.AsOfDate,
            Top5ConcentrationPct = Math.Round(ordered.Take(5).Sum(c => c.Weight ?? 0m) * 100m, 2),
            Top10ConcentrationPct = Math.Round(ordered.Take(10).Sum(c => c.Weight ?? 0m) * 100m, 2),
            SectorCount = ordered.Select(c => c.Sector).Where(s => s != "Unknown").Distinct().Count(),
            MissingSymbolCount = ordered.Count(c => string.IsNullOrWhiteSpace(c.Symbol)),
            FetchedAtUtc = snapshot.FetchedAtUtc,
            Source = snapshot.Source,
            QualityReport = snapshot.QualityReport,
        };
    }
}
