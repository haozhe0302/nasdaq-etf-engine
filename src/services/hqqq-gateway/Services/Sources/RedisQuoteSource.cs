using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Infrastructure;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hqqq.Gateway.Services.Sources;

/// <summary>
/// Reads the latest <see cref="QuoteSnapshotDto"/> for the configured basket
/// from Redis under <c>hqqq:snapshot:{basketId}</c>. This key is materialized
/// by <c>hqqq-quote-engine</c>'s <c>RedisSnapshotWriter</c> on every
/// materialize tick, so the gateway can serve latest state directly without
/// round-tripping through the legacy monolith.
///
/// On missing / malformed / transport errors we return a controlled JSON
/// error body with a non-200 status; we never silently substitute stub data
/// while explicitly in redis mode (acceptance criterion for B5).
/// </summary>
public sealed class RedisQuoteSource : IQuoteSource
{
    private readonly IGatewayRedisReader _reader;
    private readonly IOptions<GatewayOptions> _options;
    private readonly ILogger<RedisQuoteSource> _logger;

    public RedisQuoteSource(
        IGatewayRedisReader reader,
        IOptions<GatewayOptions> options,
        ILogger<RedisQuoteSource> logger)
    {
        _reader = reader;
        _options = options;
        _logger = logger;
    }

    public async Task<IResult> GetQuoteAsync(CancellationToken ct)
    {
        var basketId = _options.Value.ResolveBasketId();
        var key = RedisKeys.Snapshot(basketId);

        string? raw;
        try
        {
            raw = await _reader.StringGetAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis transport error reading quote for basket {BasketId}", basketId);
            return Results.Json(
                new { error = "quote_redis_error", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error reading quote from Redis for basket {BasketId}", basketId);
            return Results.Json(
                new { error = "quote_redis_error", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (raw is null)
        {
            _logger.LogInformation(
                "Redis quote key {Key} missing for basket {BasketId}; returning 503 degraded response",
                key, basketId);
            return Results.Json(
                new { error = "quote_unavailable", reason = "no_snapshot_in_redis", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        QuoteSnapshotDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<QuoteSnapshotDto>(raw, HqqqJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Malformed quote JSON at Redis key {Key} for basket {BasketId}", key, basketId);
            return Results.Json(
                new { error = "quote_malformed", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (dto is null)
        {
            _logger.LogWarning(
                "Quote payload at Redis key {Key} deserialized to null for basket {BasketId}", key, basketId);
            return Results.Json(
                new { error = "quote_malformed", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(dto, HqqqJsonDefaults.Options);
    }
}
