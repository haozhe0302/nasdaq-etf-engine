namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// A single data point in the Market page chart ring buffer.
/// </summary>
public sealed record SeriesPoint
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal Market { get; init; }
}
