namespace Hqqq.Domain.ValueObjects;

/// <summary>
/// Typed wrapper for the pricing-engine scale factor.
/// A scale factor of 0 (or below) is treated as uninitialized.
/// </summary>
public readonly record struct ScaleFactor(decimal Value)
{
    public static readonly ScaleFactor Uninitialized = new(0m);

    public bool IsInitialized => Value > 0m;

    public static implicit operator decimal(ScaleFactor f) => f.Value;
}
