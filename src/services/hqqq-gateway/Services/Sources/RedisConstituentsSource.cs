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
/// Reads the latest <see cref="ConstituentsSnapshotDto"/> for the configured
/// basket from Redis under <c>hqqq:constituents:{basketId}</c>, which is
/// written by <c>hqqq-quote-engine</c>'s <c>RedisConstituentsWriter</c> on
/// basket activation. Degraded responses mirror <see cref="RedisQuoteSource"/>.
/// </summary>
public sealed class RedisConstituentsSource : IConstituentsSource
{
    private readonly IGatewayRedisReader _reader;
    private readonly IOptions<GatewayOptions> _options;
    private readonly ILogger<RedisConstituentsSource> _logger;

    public RedisConstituentsSource(
        IGatewayRedisReader reader,
        IOptions<GatewayOptions> options,
        ILogger<RedisConstituentsSource> logger)
    {
        _reader = reader;
        _options = options;
        _logger = logger;
    }

    public async Task<IResult> GetConstituentsAsync(CancellationToken ct)
    {
        var basketId = _options.Value.ResolveBasketId();
        var key = RedisKeys.Constituents(basketId);

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
            _logger.LogWarning(ex,
                "Redis transport error reading constituents for basket {BasketId}", basketId);
            return Results.Json(
                new { error = "constituents_redis_error", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Unexpected error reading constituents from Redis for basket {BasketId}", basketId);
            return Results.Json(
                new { error = "constituents_redis_error", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (raw is null)
        {
            _logger.LogInformation(
                "Redis constituents key {Key} missing for basket {BasketId}; returning 503 degraded response",
                key, basketId);
            return Results.Json(
                new { error = "constituents_unavailable", reason = "no_constituents_in_redis", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        ConstituentsSnapshotDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ConstituentsSnapshotDto>(raw, HqqqJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Malformed constituents JSON at Redis key {Key} for basket {BasketId}", key, basketId);
            return Results.Json(
                new { error = "constituents_malformed", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (dto is null)
        {
            _logger.LogWarning(
                "Constituents payload at Redis key {Key} deserialized to null for basket {BasketId}", key, basketId);
            return Results.Json(
                new { error = "constituents_malformed", basketId },
                HqqqJsonDefaults.Options,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(dto, HqqqJsonDefaults.Options);
    }
}
