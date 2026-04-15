namespace Hqqq.Domain.ValueObjects;

/// <summary>
/// Typed wrapper for basket SHA-256 fingerprints.
/// </summary>
public readonly record struct Fingerprint(string Value)
{
    public override string ToString() => Value;

    public static implicit operator string(Fingerprint f) => f.Value;
}
