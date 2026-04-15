namespace Hqqq.Contracts.Events;

/// <summary>
/// Published to the compacted <c>refdata.basket.active.v1</c> topic
/// when a basket version is promoted to active.
/// Key: <see cref="BasketId"/>.
/// </summary>
public sealed record BasketActivatedV1
{
    public required string BasketId { get; init; }
    public required string Fingerprint { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required int ConstituentCount { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }
    public required string Version { get; init; }
}
