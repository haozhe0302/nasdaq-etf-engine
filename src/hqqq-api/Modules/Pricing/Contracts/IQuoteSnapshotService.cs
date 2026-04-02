namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Computes and caches the current indicative quote snapshot for the ETF.
/// </summary>
public interface IQuoteSnapshotService
{
    QuoteSnapshot? GetLatest();
    Task<QuoteSnapshot> ComputeAsync(CancellationToken ct = default);
}
