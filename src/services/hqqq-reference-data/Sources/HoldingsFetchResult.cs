namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Outcome of an <see cref="IHoldingsSource.FetchAsync"/> call.
///
/// Three terminal states:
/// <list type="bullet">
///   <item><c>Ok</c> — a validated <see cref="HoldingsSnapshot"/> is ready to activate.</item>
///   <item><c>Unavailable</c> — the source is disabled or could not be reached (not an error — triggers fallback).</item>
///   <item><c>Invalid</c> — the source responded but the payload failed validation (strict mode falls back; permissive mode may still accept).</item>
/// </list>
/// </summary>
public sealed record HoldingsFetchResult
{
    public required HoldingsFetchStatus Status { get; init; }
    public HoldingsSnapshot? Snapshot { get; init; }

    /// <summary>Human-readable explanation for <c>Unavailable</c>/<c>Invalid</c>.</summary>
    public string? Reason { get; init; }

    public static HoldingsFetchResult Ok(HoldingsSnapshot snapshot) =>
        new() { Status = HoldingsFetchStatus.Ok, Snapshot = snapshot };

    public static HoldingsFetchResult Unavailable(string reason) =>
        new() { Status = HoldingsFetchStatus.Unavailable, Reason = reason };

    public static HoldingsFetchResult Invalid(string reason) =>
        new() { Status = HoldingsFetchStatus.Invalid, Reason = reason };
}

public enum HoldingsFetchStatus
{
    Ok,
    Unavailable,
    Invalid,
}
