namespace Hqqq.Api.Modules.Pricing.Contracts;

public interface ISeriesStore
{
    Task<IReadOnlyList<SeriesPoint>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyList<SeriesPoint> points, CancellationToken ct = default);
}
