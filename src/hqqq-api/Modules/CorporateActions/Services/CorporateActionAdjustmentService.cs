using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.CorporateActions.Contracts;

namespace Hqqq.Api.Modules.CorporateActions.Services;

/// <summary>
/// Applies split adjustments to basket constituents before pricing-basis construction.
/// <list type="bullet">
///   <item>Original basket snapshots are never mutated.</item>
///   <item>Adjusted shares carry provenance via <c>SharesSource</c> suffix <c>:split-adjusted</c>.</item>
///   <item>On provider failure the service degrades gracefully (returns original shares).</item>
///   <item>Results are cached for the same basket fingerprint and runtime date.</item>
/// </list>
/// </summary>
public sealed class CorporateActionAdjustmentService : ICorporateActionAdjustmentService
{
    private readonly ICorporateActionProvider _provider;
    private readonly ILogger<CorporateActionAdjustmentService> _logger;

    private volatile AdjustmentReport? _lastReport;
    private AdjustedBasketResult? _cachedResult;
    private string? _cachedFingerprint;
    private DateOnly _cachedRuntimeDate;
    private readonly object _cacheLock = new();

    public AdjustmentReport? LastReport => _lastReport;

    public CorporateActionAdjustmentService(
        ICorporateActionProvider provider,
        ILogger<CorporateActionAdjustmentService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<AdjustedBasketResult> AdjustAsync(
        BasketSnapshot snapshot, CancellationToken ct = default)
    {
        var runtimeDate = DateOnly.FromDateTime(DateTime.UtcNow);

        lock (_cacheLock)
        {
            if (_cachedResult is not null
                && _cachedFingerprint == snapshot.Fingerprint
                && _cachedRuntimeDate == runtimeDate)
            {
                return _cachedResult;
            }
        }

        var result = await ComputeAdjustmentAsync(snapshot, runtimeDate, ct);

        lock (_cacheLock)
        {
            _cachedResult = result;
            _cachedFingerprint = snapshot.Fingerprint;
            _cachedRuntimeDate = runtimeDate;
        }

        _lastReport = result.Report;
        return result;
    }

    private async Task<AdjustedBasketResult> ComputeAdjustmentAsync(
        BasketSnapshot snapshot, DateOnly runtimeDate, CancellationToken ct)
    {
        if (runtimeDate <= snapshot.AsOfDate)
            return NoAdjustment(snapshot, runtimeDate);

        var officialSymbols = snapshot.Constituents
            .Where(c => c.SharesHeld > 0
                && !c.SharesSource.Contains("derived", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (officialSymbols.Count == 0)
            return NoAdjustment(snapshot, runtimeDate);

        IReadOnlyList<SplitEvent> splits;
        bool providerFailed = false;
        string? providerError = null;

        try
        {
            // Fetch splits strictly after basket as-of date, up to and including today.
            splits = await _provider.GetSplitsAsync(
                officialSymbols,
                snapshot.AsOfDate.AddDays(1),
                runtimeDate,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Corporate-action provider failed; proceeding with unadjusted basket");
            splits = [];
            providerFailed = true;
            providerError = ex.Message;
        }

        var splitsBySymbol = splits
            .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.EffectiveDate).ToList(),
                StringComparer.OrdinalIgnoreCase);

        if (splitsBySymbol.Count == 0)
            return NoAdjustment(snapshot, runtimeDate, providerFailed, providerError);

        var adjustments = new List<ConstituentAdjustment>();
        var adjustedConstituents = new List<BasketConstituent>(snapshot.Constituents.Count);

        foreach (var c in snapshot.Constituents)
        {
            bool hasOfficialShares = c.SharesHeld > 0
                && !c.SharesSource.Contains("derived", StringComparison.OrdinalIgnoreCase);

            if (hasOfficialShares
                && splitsBySymbol.TryGetValue(c.Symbol, out var symbolSplits)
                && symbolSplits.Count > 0)
            {
                var cumulativeFactor = 1m;
                foreach (var s in symbolSplits)
                    cumulativeFactor *= s.Factor;

                var adjustedShares = c.SharesHeld * cumulativeFactor;

                adjustments.Add(new ConstituentAdjustment
                {
                    Symbol = c.Symbol,
                    OriginalShares = c.SharesHeld,
                    AdjustedShares = adjustedShares,
                    CumulativeSplitFactor = cumulativeFactor,
                    AppliedSplits = symbolSplits,
                });

                adjustedConstituents.Add(c with
                {
                    SharesHeld = adjustedShares,
                    SharesSource = $"{c.SharesSource}:split-adjusted",
                });
            }
            else
            {
                adjustedConstituents.Add(c);
            }
        }

        foreach (var adj in adjustments)
        {
            _logger.LogInformation(
                "Split adjustment: {Symbol} shares {Orig} → {Adj} (factor {Factor:F4}, " +
                "{SplitCount} split(s), basket as-of {AsOf})",
                adj.Symbol, adj.OriginalShares, adj.AdjustedShares,
                adj.CumulativeSplitFactor, adj.AppliedSplits.Count,
                snapshot.AsOfDate);
        }

        var report = new AdjustmentReport
        {
            AdjustedConstituentCount = adjustments.Count,
            UnadjustedConstituentCount = snapshot.Constituents.Count - adjustments.Count,
            Adjustments = adjustments,
            BasketAsOfDate = snapshot.AsOfDate,
            RuntimeDate = runtimeDate,
            ProviderFailed = providerFailed,
            ProviderError = providerError,
            ComputedAtUtc = DateTimeOffset.UtcNow,
        };

        var adjustedSnapshot = snapshot with { Constituents = adjustedConstituents };

        return new AdjustedBasketResult
        {
            AdjustedSnapshot = adjustedSnapshot,
            Report = report,
        };
    }

    private static AdjustedBasketResult NoAdjustment(
        BasketSnapshot snapshot,
        DateOnly runtimeDate,
        bool providerFailed = false,
        string? providerError = null)
    {
        return new AdjustedBasketResult
        {
            AdjustedSnapshot = snapshot,
            Report = new AdjustmentReport
            {
                AdjustedConstituentCount = 0,
                UnadjustedConstituentCount = snapshot.Constituents.Count,
                Adjustments = [],
                BasketAsOfDate = snapshot.AsOfDate,
                RuntimeDate = runtimeDate,
                ProviderFailed = providerFailed,
                ProviderError = providerError,
                ComputedAtUtc = DateTimeOffset.UtcNow,
            },
        };
    }
}
