using Hqqq.Contracts.Events;

namespace Hqqq.ReferenceData.Publishing;

/// <summary>
/// Publishes the active basket state event onto Kafka so downstream
/// services (notably <c>quote-engine</c>) can activate without a
/// synchronous callback to <c>hqqq-reference-data</c>.
/// </summary>
public interface IBasketPublisher
{
    Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct);
}
