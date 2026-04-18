namespace Hqqq.Analytics.Timescale;

/// <summary>
/// Read-side view of a single <c>quote_snapshots</c> row, owned by the
/// analytics service. Mirrors the column set written by the persistence
/// service's <c>QuoteSnapshotSqlCommands.InsertSql</c> exactly; kept as a
/// separate type (rather than referencing the persistence executable) so
/// we do not chain two Worker-SDK projects and pick up duplicate entry
/// points. The SELECT projection in
/// <see cref="TimescaleQuoteSnapshotReader"/> is the authoritative contract.
/// </summary>
public sealed record QuoteSnapshotRecord
{
    public required string BasketId { get; init; }
    public required DateTimeOffset Ts { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketProxyPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required int StaleCount { get; init; }
    public required int FreshCount { get; init; }
    public required double MaxComponentAgeMs { get; init; }
    public required string QuoteQuality { get; init; }
}
