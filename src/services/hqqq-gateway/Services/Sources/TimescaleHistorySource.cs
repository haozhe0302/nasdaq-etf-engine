using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Timescale;
using Hqqq.Infrastructure.Serialization;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hqqq.Gateway.Services.Sources;

/// <summary>
/// Timescale-backed <see cref="IHistorySource"/> that serves
/// <c>/api/history?range=...</c> directly from the
/// <c>quote_snapshots</c> hypertable. Preserves the existing frontend
/// response contract (see <see cref="HistoryResponse"/>) exactly.
/// </summary>
/// <remarks>
/// Failure policy:
/// <list type="bullet">
///   <item><description>Unsupported range → HTTP 400 <c>{"error":"history_range_unsupported"}</c>.</description></item>
///   <item><description>Empty window → HTTP 200 with an empty, render-safe payload.</description></item>
///   <item><description>Query failure → HTTP 503 <c>{"error":"history_unavailable"}</c>; never silently falls back to stub.</description></item>
/// </list>
/// </remarks>
public sealed class TimescaleHistorySource : IHistorySource
{
    private readonly ITimescaleHistoryQueryService _queryService;
    private readonly IOptions<GatewayOptions> _options;
    private readonly ILogger<TimescaleHistorySource> _logger;
    private readonly TimeProvider _timeProvider;

    public TimescaleHistorySource(
        ITimescaleHistoryQueryService queryService,
        IOptions<GatewayOptions> options,
        ILogger<TimescaleHistorySource> logger,
        TimeProvider? timeProvider = null)
    {
        _queryService = queryService;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IResult> GetHistoryAsync(string? range, CancellationToken ct)
    {
        var basketId = _options.Value.ResolveBasketId();
        var todayUtc = _timeProvider.GetUtcNow();

        if (!HistoryRangeMap.TryResolve(range, todayUtc, out var normalizedRange, out var fromUtc, out var toUtc))
        {
            _logger.LogInformation(
                "Rejecting /api/history call with unsupported range {Range}", range);
            return Results.Json(
                new
                {
                    error = "history_range_unsupported",
                    range,
                    supported = HistoryRangeMap.SupportedRanges,
                },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status400BadRequest);
        }

        IReadOnlyList<HistoryRow> rows;
        try
        {
            rows = await _queryService
                .LoadAsync(basketId, fromUtc, toUtc, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex,
                "Timescale transport error loading history for basket {BasketId} range {Range}",
                basketId, normalizedRange);
            return Results.Json(
                new { error = "history_unavailable", basketId, range = normalizedRange },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error loading history for basket {BasketId} range {Range}",
                basketId, normalizedRange);
            return Results.Json(
                new { error = "history_unavailable", basketId, range = normalizedRange },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var from = new DateOnly(fromUtc.Year, fromUtc.Month, fromUtc.Day);
        var to = new DateOnly(toUtc.Year, toUtc.Month, toUtc.Day);
        var payload = HistoryResponseBuilder.Build(normalizedRange, from, to, rows);

        return Results.Json(payload, HqqqJsonDefaults.Options);
    }
}
