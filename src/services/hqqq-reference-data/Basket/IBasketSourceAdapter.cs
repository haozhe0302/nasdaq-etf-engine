namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Uniform contract for a single upstream basket source. Each adapter
/// fetches its own wire format, normalizes into a strongly-typed raw
/// result, and never throws — failures surface as a
/// <see cref="BasketSourceOutcome"/> with <c>Success=false</c>.
/// </summary>
/// <remarks>
/// <para>
/// The contract is shaped after the Phase 1 adapters in
/// <c>src/hqqq-api/Modules/Basket/Services</c>. Only the JSON adapters
/// (<see cref="AlphaVantageBasketAdapter"/>, <see cref="NasdaqBasketAdapter"/>)
/// are ported as Phase 2 production sources. The Phase 1 HTML scrapers
/// (StockAnalysis, Schwab) are intentionally out of scope for Phase 2 so
/// the deployment gate does not depend on HtmlAgilityPack or live page
/// scraping. They could be re-introduced later behind this same
/// interface without touching the pipeline.
/// </para>
/// <para>
/// The <typeparamref name="TResult"/> payload is an opaque, JSON-
/// serializable record used by <c>RawSourceCache</c>. The pipeline only
/// projects it into <see cref="AnchorBlock"/> / <see cref="TailBlock"/>
/// via the adapter's own shape-specific helper; callers never inspect
/// the raw record directly.
/// </para>
/// </remarks>
public interface IBasketSourceAdapter<TResult>
    where TResult : class
{
    /// <summary>Stable identifier, also used as the raw-cache file name.</summary>
    string Name { get; }

    /// <summary>Is this adapter enabled by configuration?</summary>
    bool Enabled { get; }

    /// <summary>Performs a single live fetch. Never throws — wrap in <see cref="BasketSourceOutcome{T}"/>.</summary>
    Task<BasketSourceOutcome<TResult>> FetchAsync(CancellationToken ct);
}

/// <summary>Outcome wrapper returned by <see cref="IBasketSourceAdapter{TResult}"/>.</summary>
public sealed record BasketSourceOutcome<TResult>
    where TResult : class
{
    /// <summary>Adapter name (maps to <see cref="IBasketSourceAdapter{TResult}.Name"/>).</summary>
    public required string Source { get; init; }

    /// <summary>True iff a usable payload was obtained (live OR raw-cache).</summary>
    public required bool Success { get; init; }

    /// <summary>Where the payload originated — <c>live</c>, <c>raw-cache</c>, <c>failed</c>, <c>disabled</c>.</summary>
    public required string Origin { get; init; }

    public TResult? Payload { get; init; }
    public int RowCount { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static BasketSourceOutcome<TResult> Disabled(string source) =>
        new() { Source = source, Success = false, Origin = "disabled", RowCount = 0 };

    public static BasketSourceOutcome<TResult> Live(string source, TResult payload, int rowCount) =>
        new()
        {
            Source = source,
            Success = true,
            Origin = "live",
            Payload = payload,
            RowCount = rowCount,
        };

    public static BasketSourceOutcome<TResult> Failed(string source, string error) =>
        new()
        {
            Source = source,
            Success = false,
            Origin = "failed",
            Error = error,
        };
}
