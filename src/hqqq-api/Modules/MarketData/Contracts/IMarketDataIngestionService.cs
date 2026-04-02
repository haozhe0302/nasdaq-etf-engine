namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Manages the lifecycle of market-data ingestion (WebSocket + REST fallback).
/// </summary>
public interface IMarketDataIngestionService
{
    Task StartAsync(IEnumerable<string> symbols, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    bool IsRunning { get; }
}
