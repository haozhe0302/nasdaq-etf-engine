namespace Hqqq.Api.Modules.History.Contracts;

/// <summary>
/// A single persisted quote snapshot row in the date-partitioned JSONL history.
/// Stored at data/history/YYYY-MM-DD/quotes.jsonl (one JSON object per line).
/// </summary>
public sealed record HistoryRow
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketPrice { get; init; }
    public int SymbolsTotal { get; init; }
    public int SymbolsStale { get; init; }
}
