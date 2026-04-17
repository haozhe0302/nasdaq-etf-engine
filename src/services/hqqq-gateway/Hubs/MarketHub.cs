using Microsoft.AspNetCore.SignalR;

namespace Hqqq.Gateway.Hubs;

/// <summary>
/// SignalR hub for real-time market data streaming.
/// Currently a skeleton — no live data is broadcast yet. Phase 2B5 cut the
/// REST quote/constituents endpoints over to Redis but intentionally leaves
/// the live fan-out path untouched; that work lands in D2.
/// </summary>
public sealed class MarketHub : Hub
{
    // TODO: Phase 2D2 — broadcast live quote updates using event name "QuoteUpdate"
    //   Clients.All.SendAsync("QuoteUpdate", payload)
    // TODO: Phase 2D2 — add Redis pub/sub backplane for multi-instance fan-out
    //   builder.AddStackExchangeRedis(...) and subscribe to RedisKeys.SnapshotChannel

    private readonly ILogger<MarketHub> _logger;

    public MarketHub(ILogger<MarketHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
