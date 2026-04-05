namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Feed and pipeline status for the quote snapshot.
/// </summary>
public sealed record FeedInfo
{
    public required bool WebSocketConnected { get; init; }
    public required bool FallbackActive { get; init; }
    public required bool PricingActive { get; init; }
    public required string BasketState { get; init; }
    public required bool PendingActivationBlocked { get; init; }
    public string? PendingBlockedReason { get; init; }

    public string? MarketSessionState { get; init; }
    public bool? IsRegularSessionOpen { get; init; }
    public bool? IsTradingDay { get; init; }
    public DateTimeOffset? NextOpenUtc { get; init; }
    public string? SessionLabel { get; init; }
}
