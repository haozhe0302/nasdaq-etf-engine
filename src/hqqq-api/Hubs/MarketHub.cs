using Microsoft.AspNetCore.SignalR;

namespace Hqqq.Api.Hubs;

/// <summary>
/// SignalR hub for real-time market data and quote broadcasts.
/// Clients connect to <c>/hubs/market</c>.
/// Live price broadcasting will be wired in a subsequent phase.
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
