namespace Hqqq.Domain.Services;

/// <summary>
/// Pure freshness summary result. Engine-layer code translates this
/// into the serving DTO shape.
/// </summary>
public sealed record FreshnessSummary
{
    public required int SymbolsTotal { get; init; }
    public required int SymbolsFresh { get; init; }
    public required int SymbolsStale { get; init; }
    public required decimal FreshPct { get; init; }
    public DateTimeOffset? LastTickUtc { get; init; }
    public double? AvgTickIntervalMs { get; init; }
}

/// <summary>
/// One observation used as input to <see cref="FreshnessSummarizer"/>.
/// </summary>
public readonly record struct SymbolFreshnessInput(string Symbol, DateTimeOffset ReceivedAtUtc);
