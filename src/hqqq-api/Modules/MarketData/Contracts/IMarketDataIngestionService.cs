namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Exposes the runtime state of the market-data ingestion pipeline.
/// Lifecycle is managed by the hosted-service infrastructure.
/// </summary>
public interface IMarketDataIngestionService
{
    bool IsRunning { get; }
    bool IsWebSocketConnected { get; }
    bool IsFallbackActive { get; }
    DateTimeOffset? LastActivityUtc { get; }

    string? LastUpstreamError { get; }
    int? LastUpstreamErrorCode { get; }
    DateTimeOffset? LastUpstreamErrorAtUtc { get; }
}
