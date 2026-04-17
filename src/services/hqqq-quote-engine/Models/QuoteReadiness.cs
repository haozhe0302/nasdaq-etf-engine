namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// Discrete readiness states emitted in <c>QuoteSnapshotDto.QuoteState</c>.
/// Mirrors the strings the legacy monolith surfaces to the frontend so the
/// existing adapter continues to work unchanged.
/// </summary>
public enum QuoteReadiness
{
    /// <summary>Engine has no basket or no scale factor yet.</summary>
    Uninitialized,

    /// <summary>Regular session, ticks flowing.</summary>
    Live,

    /// <summary>Regular session but every tracked symbol is stale.</summary>
    FrozenAllStale,

    /// <summary>Outside regular session.</summary>
    Closed,
}

internal static class QuoteReadinessStrings
{
    public const string Uninitialized = "uninitialized";
    public const string Live = "live";
    public const string FrozenAllStale = "frozen_all_stale";
    public const string Closed = "closed";

    public static string ToWireValue(this QuoteReadiness readiness) => readiness switch
    {
        QuoteReadiness.Uninitialized => Uninitialized,
        QuoteReadiness.Live => Live,
        QuoteReadiness.FrozenAllStale => FrozenAllStale,
        QuoteReadiness.Closed => Closed,
        _ => Uninitialized,
    };
}
