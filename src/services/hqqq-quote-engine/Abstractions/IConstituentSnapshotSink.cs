using Hqqq.Contracts.Dtos;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Sink for the latest materialized constituents snapshot. Mirrors
/// <see cref="IQuoteSnapshotSink"/> but carries basket composition + quality
/// rather than iNAV scalars. Same latest-state semantics: the store retains
/// only the most recent payload per basket.
/// </summary>
public interface IConstituentSnapshotSink
{
    Task WriteAsync(string basketId, ConstituentsSnapshotDto snapshot, CancellationToken ct);
}
