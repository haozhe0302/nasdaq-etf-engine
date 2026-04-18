using Hqqq.Contracts.Dtos;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Orchestration boundary of the internal pricing engine:
/// feed intake → state mutation → snapshot / delta materialization.
/// Transport wiring (Kafka, Redis, SignalR) lives outside this interface
/// and is deferred to later B-phases.
/// </summary>
public interface IQuoteEngine
{
    /// <summary>True once both a basket and a scale factor are in place.</summary>
    bool IsInitialized { get; }

    /// <summary>Apply a normalized tick to per-symbol state.</summary>
    void OnTick(NormalizedTick tick);

    /// <summary>Install a new active basket (first activation or replacement).</summary>
    void OnBasketActivated(ActiveBasket basket);

    /// <summary>Build a full serving snapshot (REST shape) from current state.</summary>
    QuoteSnapshotDto? BuildSnapshot();

    /// <summary>Build a slim realtime delta (SignalR shape) from current state.</summary>
    QuoteUpdateDto? BuildDelta();
}
