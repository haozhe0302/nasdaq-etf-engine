using Microsoft.AspNetCore.SignalR;

namespace Hqqq.Gateway.Hubs;

/// <summary>
/// SignalR hub for real-time market data streaming. The hub itself stays
/// minimal: clients connect, listen for the <c>"QuoteUpdate"</c> event, and
/// re-bootstrap from REST <c>GET /api/quote</c> on reconnect. Live fan-out
/// is driven externally by
/// <see cref="Services.Realtime.QuoteUpdateSubscriber"/>, which subscribes
/// to the Redis pub/sub channel populated by hqqq-quote-engine and
/// broadcasts <c>QuoteUpdate</c> via <see cref="Microsoft.AspNetCore.SignalR.IHubContext{THub}"/>.
/// </summary>
public sealed class MarketHub : Hub
{
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
