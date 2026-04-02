namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Persistence layer for <see cref="ScaleState"/>. Implementations must be
/// safe for concurrent access from multiple threads.
/// </summary>
public interface IScaleStateStore
{
    Task<ScaleState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ScaleState state, CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}
