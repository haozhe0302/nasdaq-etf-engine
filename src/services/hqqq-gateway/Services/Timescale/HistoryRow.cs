namespace Hqqq.Gateway.Services.Timescale;

/// <summary>
/// Row shape returned by <see cref="ITimescaleHistoryQueryService"/>.
/// Mirrors the read-only subset of the <c>quote_snapshots</c> hypertable
/// that the gateway needs to build <c>/api/history</c> responses.
/// </summary>
public sealed record HistoryRow(
    DateTimeOffset Ts,
    decimal Nav,
    decimal MarketProxyPrice);
