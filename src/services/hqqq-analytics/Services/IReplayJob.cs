using Hqqq.Analytics.Options;

namespace Hqqq.Analytics.Services;

/// <summary>
/// Future seam for a tick-to-snapshot replay job. Reserved for Phase 2C5+:
/// replays persisted <c>raw_ticks</c> through the quote-engine's snapshot
/// computation and writes reconstructed snapshots back to Timescale (or a
/// shadow table) for comparison against the originals. Intentionally not
/// implemented in C4 — this interface exists so the dispatcher, DI wiring,
/// and tests can evolve additively without reshaping the options surface.
/// </summary>
public interface IReplayJob
{
    Task RunAsync(AnalyticsOptions options, CancellationToken ct);
}
