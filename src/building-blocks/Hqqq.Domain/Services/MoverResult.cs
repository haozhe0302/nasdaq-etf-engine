namespace Hqqq.Domain.Services;

/// <summary>
/// Pure mover computation result. Engine-layer code translates this
/// into the serving DTO shape; keeping it domain-side avoids a
/// Hqqq.Contracts dependency from Hqqq.Domain.
/// </summary>
public sealed record MoverResult
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required decimal ChangePct { get; init; }
    public required decimal Impact { get; init; }

    /// <summary>"up" or "down".</summary>
    public required string Direction { get; init; }
}
