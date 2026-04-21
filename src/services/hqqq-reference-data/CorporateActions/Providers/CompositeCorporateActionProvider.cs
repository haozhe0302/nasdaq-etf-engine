using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.CorporateActions.Providers;

/// <summary>
/// Two-arm composite corp-action provider. Always runs the
/// <see cref="FileCorporateActionProvider"/> (deterministic baseline).
/// When <c>ReferenceData:CorporateActions:Tiingo:Enabled=true</c> it
/// additionally queries <see cref="TiingoCorporateActionProvider"/> and
/// overlays its split rows on top of the file baseline, dedup-ed by
/// <c>(Symbol, EffectiveDate)</c>.
/// </summary>
/// <remarks>
/// The composite never throws. If the Tiingo arm fails we log, set the
/// lineage tag to <c>"file+tiingo-degraded"</c>, and return the file-only
/// feed. Renames currently come from the file provider only; Tiingo's EOD
/// endpoint does not surface ticker rename metadata in a format we can
/// consume reliably.
/// </remarks>
public sealed class CompositeCorporateActionProvider : ICorporateActionProvider
{
    private readonly FileCorporateActionProvider _file;
    private readonly TiingoCorporateActionProvider _tiingo;
    private readonly TiingoCorporateActionOptions _tiingoOptions;
    private readonly ILogger<CompositeCorporateActionProvider> _logger;

    public CompositeCorporateActionProvider(
        FileCorporateActionProvider file,
        TiingoCorporateActionProvider tiingo,
        IOptions<ReferenceDataOptions> options,
        ILogger<CompositeCorporateActionProvider> logger)
    {
        _file = file;
        _tiingo = tiingo;
        _tiingoOptions = options.Value.CorporateActions.Tiingo;
        _logger = logger;
    }

    public string Name => _tiingoOptions.Enabled ? "file+tiingo" : "file";

    public async Task<CorporateActionFeed> FetchAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var fileFeed = await _file.FetchAsync(symbols, from, to, ct).ConfigureAwait(false);

        if (!_tiingoOptions.Enabled)
        {
            return fileFeed;
        }

        CorporateActionFeed tiingoFeed;
        try
        {
            tiingoFeed = await _tiingo.FetchAsync(symbols, from, to, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CompositeCorporateActionProvider: Tiingo arm threw; falling back to file-only");
            return fileFeed with { Source = "file+tiingo-degraded", Error = ex.Message };
        }

        if (tiingoFeed.Error is not null)
        {
            return fileFeed with
            {
                Source = "file+tiingo-degraded",
                Error = tiingoFeed.Error,
            };
        }

        // Merge splits: file provides the deterministic baseline; Tiingo
        // overlays extra rows. De-dupe by (symbol, date) so the same split
        // isn't double-counted.
        var seen = fileFeed.Splits
            .Select(s => (s.Symbol, s.EffectiveDate))
            .ToHashSet();
        var merged = new List<SplitEvent>(fileFeed.Splits);
        foreach (var ts in tiingoFeed.Splits)
        {
            if (seen.Add((ts.Symbol, ts.EffectiveDate)))
            {
                merged.Add(ts);
            }
        }

        return new CorporateActionFeed
        {
            Splits = merged,
            // Renames continue to come from the file provider only.
            Renames = fileFeed.Renames,
            Source = "file+tiingo",
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
