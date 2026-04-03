namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Aggregate health status of the market-data ingestion pipeline,
/// including pending-basket activation readiness metrics.
/// </summary>
public sealed record FeedHealthSnapshot
{
    public required bool WebSocketConnected { get; init; }
    public required bool FallbackActive { get; init; }
    public required int SymbolsTracked { get; init; }
    public required int SymbolsWithPrice { get; init; }
    public required int StaleSymbolCount { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }

    public required int ActiveSymbolCount { get; init; }
    public required int PendingSymbolCount { get; init; }
    public required int ActiveWithPriceCount { get; init; }
    public required int PendingWithPriceCount { get; init; }
    public required int StaleActiveCount { get; init; }
    public required int StalePendingCount { get; init; }
    public required decimal ActiveCoveragePct { get; init; }
    public required decimal PendingCoveragePct { get; init; }

    public DateTimeOffset? LastUpstreamActivityUtc { get; init; }
    public double? AverageTickIntervalMs { get; init; }

    /// <summary>
    /// True when the pending basket has sufficient live, non-stale price coverage
    /// for safe activation at market open (&gt;= 95% coverage, &lt;= 5% stale).
    /// </summary>
    public required bool IsPendingBasketReady { get; init; }
}
