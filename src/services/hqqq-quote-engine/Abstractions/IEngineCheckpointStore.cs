using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Pluggable persistence boundary for the engine's crash-recovery checkpoint.
/// The B3 implementation is a local JSON file; later phases may swap in a
/// Redis- or Timescale-backed store without touching engine internals.
/// </summary>
public interface IEngineCheckpointStore
{
    /// <summary>
    /// Loads the most recent checkpoint, or returns <c>null</c> if none
    /// exists or the payload is unreadable. Must not throw on missing or
    /// corrupt files — startup survives a lost checkpoint.
    /// </summary>
    ValueTask<EngineCheckpoint?> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists the given checkpoint. Implementations should aim for atomic
    /// writes so a crash mid-save cannot produce a half-written file that
    /// defeats the next restore.
    /// </summary>
    ValueTask SaveAsync(EngineCheckpoint checkpoint, CancellationToken ct);
}
