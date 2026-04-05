namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Point-in-time market session state for NYSE.
/// Possible <see cref="State"/> values:
/// <c>pre_market</c>, <c>regular_open</c>, <c>after_hours</c>,
/// <c>weekend</c>, <c>holiday</c>, <c>early_close_closed</c>.
/// </summary>
public sealed record MarketSessionSnapshot
{
    public required string State { get; init; }
    public required string Label { get; init; }
    public required bool IsRegularSessionOpen { get; init; }
    public required bool IsTradingDay { get; init; }
    public DateTimeOffset? NextOpenUtc { get; init; }
}
