using System.Text.Json;
using Hqqq.QuoteEngine.Services;
using StackExchange.Redis;

namespace Hqqq.QuoteEngine.Persistence;

/// <summary>
/// Default <see cref="ICalibrationStore"/> backed by a shared
/// <see cref="IConnectionMultiplexer"/>. Each basket maps to the Redis
/// string <c>hqqq:calibration:{basketId}</c> carrying a JSON-encoded
/// <see cref="CalibrationRecord"/>. TTL defaults to
/// <see cref="QuoteEngineOptions.CalibrationTtl"/> (7 days).
/// </summary>
/// <remarks>
/// The store is best-effort: <see cref="TryGet"/> and <see cref="Set"/>
/// both swallow transient Redis failures and log at Warning level. The
/// engine is designed so a missing record degrades to a fresh bootstrap
/// (≈ one QQQ tick window) rather than a hard failure.
/// </remarks>
public sealed class RedisCalibrationStore : ICalibrationStore
{
    private const string KeyPrefix = "hqqq:calibration:";

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly QuoteEngineOptions _options;
    private readonly ILogger<RedisCalibrationStore> _logger;

    public RedisCalibrationStore(
        IConnectionMultiplexer multiplexer,
        QuoteEngineOptions options,
        ILogger<RedisCalibrationStore> logger)
    {
        _multiplexer = multiplexer;
        _options = options;
        _logger = logger;
    }

    public CalibrationRecord? TryGet(string basketId)
    {
        if (string.IsNullOrWhiteSpace(basketId)) return null;
        try
        {
            var db = _multiplexer.GetDatabase();
            var raw = db.StringGet(Key(basketId));
            if (raw.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<CalibrationRecord>((string)raw!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RedisCalibrationStore: failed to read calibration for {BasketId}; degrading to bootstrap",
                basketId);
            return null;
        }
    }

    public void Set(CalibrationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var db = _multiplexer.GetDatabase();
            var payload = JsonSerializer.Serialize(record);
            db.StringSet(Key(record.BasketId), payload, _options.CalibrationTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RedisCalibrationStore: failed to persist calibration for {BasketId} fp={Fingerprint}; in-memory calibration still active",
                record.BasketId, record.Fingerprint);
        }
    }

    private static string Key(string basketId) => $"{KeyPrefix}{basketId}";
}
