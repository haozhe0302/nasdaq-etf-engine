using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.CorporateActions.Services;

/// <summary>
/// Phase-2-native corporate-action adjustment layer for
/// <c>hqqq-reference-data</c>. Given a freshly-fetched
/// <see cref="HoldingsSnapshot"/> and a provider feed, it:
/// <list type="number">
///   <item>collects splits in the window <c>(snapshot.AsOfDate, runtimeDate]</c>,</item>
///   <item>applies the cumulative split factor per symbol to
///         <c>SharesHeld</c>,</item>
///   <item>applies terminal ticker renames via <see cref="SymbolRemapResolver"/>,</item>
///   <item>emits a full <see cref="AdjustmentReport"/> audit trail, and</item>
///   <item>returns a lineage-tagged adjusted snapshot
///         (<c>Source = "{original}+corp-adjusted"</c> when any change was made).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The original snapshot is never mutated — records are immutable and
/// the service returns a cloned-with-overrides value. The adjustment is
/// deterministic given (snapshot, feed, runtimeDate), so callers can
/// safely cache on fingerprint if desired (the pipeline currently re-
/// computes every refresh to keep the logic straight).
/// </para>
/// <para>
/// <b>Honest scope:</b>
/// forward splits (factor &gt; 1), reverse splits (factor &lt; 1),
/// terminal ticker renames with chained-hop resolution. Dividends,
/// spin-offs, mergers, cross-exchange moves, and ISIN/CUSIP-level remaps
/// are out of scope.
/// </para>
/// </remarks>
public sealed class CorporateActionAdjustmentService
{
    private readonly ICorporateActionProvider _provider;
    private readonly TimeProvider _clock;
    private readonly CorporateActionOptions _options;
    private readonly ILogger<CorporateActionAdjustmentService> _logger;

    public CorporateActionAdjustmentService(
        ICorporateActionProvider provider,
        IOptions<ReferenceDataOptions> options,
        ILogger<CorporateActionAdjustmentService> logger,
        TimeProvider? clock = null)
    {
        _provider = provider;
        _options = options.Value.CorporateActions;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<AdjustedResult> AdjustAsync(HoldingsSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var runtimeDate = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var appliedAt = _clock.GetUtcNow();

        if (runtimeDate <= snapshot.AsOfDate)
        {
            return new AdjustedResult(
                snapshot,
                AdjustmentReport.Empty(_provider.Name, snapshot.AsOfDate, runtimeDate, appliedAt));
        }

        var lookback = DateOnly.FromDateTime(
            runtimeDate.ToDateTime(TimeOnly.MinValue).AddDays(-Math.Max(1, _options.LookbackDays)));
        var from = snapshot.AsOfDate < lookback
            ? lookback.AddDays(1)
            : snapshot.AsOfDate.AddDays(1);

        var symbols = snapshot.Constituents
            .Select(c => c.Symbol.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        CorporateActionFeed feed;
        try
        {
            feed = await _provider.FetchAsync(symbols, from, runtimeDate, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CorporateActionAdjustmentService: provider {Name} threw; proceeding without adjustment",
                _provider.Name);
            return new AdjustedResult(
                snapshot,
                AdjustmentReport.Empty(_provider.Name, snapshot.AsOfDate, runtimeDate, appliedAt, ex.Message));
        }

        if (feed.Splits.Count == 0 && feed.Renames.Count == 0)
        {
            return new AdjustedResult(
                snapshot,
                AdjustmentReport.Empty(feed.Source, snapshot.AsOfDate, runtimeDate, appliedAt, feed.Error));
        }

        // Group splits by symbol (upper-case, in order of effective date).
        var splitsBySymbol = feed.Splits
            .Where(s => s.EffectiveDate > snapshot.AsOfDate && s.EffectiveDate <= runtimeDate)
            .Where(s => s.Factor > 0m && s.Factor != 1m)
            .GroupBy(s => s.Symbol, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.EffectiveDate).ToList(),
                StringComparer.Ordinal);

        var remap = SymbolRemapResolver.Build(feed.Renames, snapshot.AsOfDate, runtimeDate);

        var splitAdjustments = new List<ConstituentAdjustment>();
        var renameAdjustments = new List<RenameAdjustment>();
        var adjustedConstituents = new List<HoldingsConstituent>(snapshot.Constituents.Count);

        foreach (var c in snapshot.Constituents)
        {
            var originalSymbolUpper = c.Symbol.Trim().ToUpperInvariant();
            var current = c with { Symbol = originalSymbolUpper };

            // 1. Splits (applied on the symbol as it appears in the snapshot).
            if (splitsBySymbol.TryGetValue(originalSymbolUpper, out var applicable)
                && applicable.Count > 0)
            {
                var factor = 1m;
                foreach (var s in applicable) factor *= s.Factor;

                var adjustedShares = Math.Round(current.SharesHeld * factor, 6, MidpointRounding.AwayFromZero);
                splitAdjustments.Add(new ConstituentAdjustment
                {
                    Symbol = originalSymbolUpper,
                    OriginalShares = c.SharesHeld,
                    AdjustedShares = adjustedShares,
                    CumulativeFactor = factor,
                    AppliedSplits = applicable,
                });

                current = current with { SharesHeld = adjustedShares };

                _logger.LogInformation(
                    "Split adjustment: {Symbol} shares {Orig} → {Adj} (factor {Factor:F4}, {SplitCount} split(s))",
                    originalSymbolUpper, c.SharesHeld, adjustedShares, factor, applicable.Count);
            }

            // 2. Renames (applied after split, keyed by current symbol).
            if (remap.TryResolve(originalSymbolUpper, out var terminal, out var renames)
                && !string.Equals(terminal, originalSymbolUpper, StringComparison.Ordinal))
            {
                renameAdjustments.Add(new RenameAdjustment
                {
                    OldSymbol = originalSymbolUpper,
                    NewSymbol = terminal,
                    AppliedRenames = renames,
                });
                current = current with { Symbol = terminal };

                _logger.LogInformation(
                    "Rename adjustment: {Old} → {New} ({HopCount} hop(s))",
                    originalSymbolUpper, terminal, renames.Count);
            }

            adjustedConstituents.Add(current);
        }

        var anyChange = splitAdjustments.Count > 0 || renameAdjustments.Count > 0;
        var adjustedSnapshot = anyChange
            ? snapshot with
            {
                Constituents = adjustedConstituents,
                Source = $"{snapshot.Source}+corp-adjusted",
            }
            : snapshot;

        var report = new AdjustmentReport
        {
            SplitAdjustments = splitAdjustments,
            RenameAdjustments = renameAdjustments,
            AddedSymbols = Array.Empty<string>(),
            RemovedSymbols = Array.Empty<string>(),
            BasketAsOfDate = snapshot.AsOfDate,
            RuntimeDate = runtimeDate,
            AppliedAtUtc = appliedAt,
            Source = feed.Source,
            ProviderError = feed.Error,
        };

        return new AdjustedResult(adjustedSnapshot, report);
    }
}

/// <summary>Return type for <see cref="CorporateActionAdjustmentService.AdjustAsync"/>.</summary>
public sealed record AdjustedResult(HoldingsSnapshot Snapshot, AdjustmentReport Report);
