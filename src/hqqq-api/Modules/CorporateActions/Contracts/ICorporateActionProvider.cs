namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// Abstraction over a corporate-action data source (e.g. Tiingo, a local file, or a cache).
/// Implementations must be safe for concurrent access.
/// </summary>
public interface ICorporateActionProvider
{
    /// <summary>
    /// Returns all stock-split events for the given symbols whose effective date
    /// falls within [<paramref name="fromDate"/>, <paramref name="toDate"/>] inclusive.
    /// </summary>
    Task<IReadOnlyList<SplitEvent>> GetSplitsAsync(
        IEnumerable<string> symbols,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default);
}
