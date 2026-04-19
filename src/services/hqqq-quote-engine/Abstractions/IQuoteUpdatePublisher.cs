using Hqqq.Contracts.Dtos;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Phase 2D2 — publishes a slim realtime <see cref="QuoteUpdateDto"/> to the
/// inter-service notification path consumed by every gateway instance for
/// SignalR fan-out on <c>/hubs/market</c>. Implementations must isolate
/// transport failures: a failed publish must log and increment a counter,
/// never crash the engine and never block future materialization cycles.
/// </summary>
public interface IQuoteUpdatePublisher
{
    Task PublishAsync(string basketId, QuoteUpdateDto update, CancellationToken ct);
}
