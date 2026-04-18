using Hqqq.Analytics.Options;
using Hqqq.Analytics.Reports;
using Hqqq.Analytics.Services;
using Hqqq.Analytics.Timescale;
using Microsoft.Extensions.Options;

namespace Hqqq.Analytics.Workers;

/// <summary>
/// One-shot C4 report job. Loads persisted snapshots (and optionally a cheap
/// raw-tick aggregate) for the configured basket/window, computes a pure
/// <see cref="ReportSummary"/>, logs it, optionally writes a JSON artifact,
/// and returns. The hosting <see cref="ReportJobDispatcher"/> is responsible
/// for stopping the application once this returns (or throws) so the host
/// exits cleanly.
/// </summary>
public sealed class SnapshotQualityReportJob : IReportJob
{
    private readonly IQuoteSnapshotReader _snapshotReader;
    private readonly IRawTickAggregateReader _rawTickReader;
    private readonly JsonReportEmitter _emitter;
    private readonly AnalyticsOptions _options;
    private readonly ILogger<SnapshotQualityReportJob> _logger;

    public SnapshotQualityReportJob(
        IQuoteSnapshotReader snapshotReader,
        IRawTickAggregateReader rawTickReader,
        JsonReportEmitter emitter,
        IOptions<AnalyticsOptions> options,
        ILogger<SnapshotQualityReportJob> logger)
    {
        _snapshotReader = snapshotReader;
        _rawTickReader = rawTickReader;
        _emitter = emitter;
        _options = options.Value;
        _logger = logger;
    }

    public string Mode => AnalyticsOptions.ReportMode;

    public async Task RunAsync(CancellationToken ct)
    {
        // Validator guarantees these are populated in report mode.
        var startUtc = _options.StartUtc ?? throw new InvalidOperationException(
            "Analytics:StartUtc is required when Mode=report.");
        var endUtc = _options.EndUtc ?? throw new InvalidOperationException(
            "Analytics:EndUtc is required when Mode=report.");

        _logger.LogInformation(
            "SnapshotQualityReportJob starting: basket={Basket} window=[{Start:O}, {End:O}] maxRows={MaxRows}",
            _options.BasketId, startUtc, endUtc, _options.MaxRows);

        var rows = await _snapshotReader.LoadAsync(
            _options.BasketId, startUtc, endUtc, _options.MaxRows, ct).ConfigureAwait(false);

        long? rawTickCount = null;
        if (_options.IncludeRawTickAggregates)
        {
            rawTickCount = await _rawTickReader.CountAsync(startUtc, endUtc, ct).ConfigureAwait(false);
            _logger.LogInformation("Raw-tick aggregate (window): count={Count}", rawTickCount);
        }

        var summary = SnapshotQualityCalculator.Compute(
            _options.BasketId,
            startUtc,
            endUtc,
            rows,
            _options.StaleQualityStates,
            _options.TopGapCount,
            rawTickCount);

        LogSummary(summary);

        if (!string.IsNullOrWhiteSpace(_options.EmitJsonPath))
        {
            try
            {
                await _emitter.EmitAsync(summary, _options.EmitJsonPath, ct).ConfigureAwait(false);
                _logger.LogInformation("Report artifact written: path={Path}", _options.EmitJsonPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write report artifact to {Path}", _options.EmitJsonPath);
                throw;
            }
        }
    }

    private void LogSummary(ReportSummary s)
    {
        if (!s.HasData)
        {
            _logger.LogWarning(
                "Report summary: basket={Basket} window=[{Start:O}, {End:O}] — NO DATA in requested window",
                s.BasketId, s.RequestedStartUtc, s.RequestedEndUtc);
            return;
        }

        _logger.LogInformation(
            "Report summary: basket={Basket} window=[{Start:O}, {End:O}] points={Points} first={First:O} last={Last:O}",
            s.BasketId, s.RequestedStartUtc, s.RequestedEndUtc, s.PointCount, s.ActualFirstUtc, s.ActualLastUtc);

        _logger.LogInformation(
            "Density: medianIntervalMs={Median} p95IntervalMs={P95} pointsPerMinute={Ppm}",
            s.MedianIntervalMs, s.P95IntervalMs, s.PointsPerMinute);

        _logger.LogInformation(
            "Quality: staleRatio={Stale} ageP50Ms={AgeP50} ageP95Ms={AgeP95} ageMaxMs={AgeMax} qualityCounts={Counts}",
            s.StaleRatio, s.MaxComponentAgeMsP50, s.MaxComponentAgeMsP95, s.MaxComponentAgeMsMax, s.QuoteQualityCounts);

        _logger.LogInformation(
            "Basis: rmseBps={Rmse} maxAbsBps={MaxAbs} avgAbsBps={AvgAbs} correlation={Corr}",
            s.RmseBps, s.MaxAbsBasisBps, s.AvgAbsBasisBps, s.Correlation);

        _logger.LogInformation(
            "Coverage: tradingDays={TradingDays} daysCovered={Days} largestGapCount={GapCount}",
            s.TradingDaysCovered, s.DaysCovered, s.LargestGaps.Count);

        for (int i = 0; i < s.LargestGaps.Count; i++)
        {
            var g = s.LargestGaps[i];
            _logger.LogInformation(
                "  gap[{Idx}] start={Start:O} end={End:O} durationMs={Duration}",
                i, g.StartUtc, g.EndUtc, g.DurationMs);
        }
    }
}
