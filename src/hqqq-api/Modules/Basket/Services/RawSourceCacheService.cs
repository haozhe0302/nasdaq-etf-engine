using System.Text.Json;
using Hqqq.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Persists per-source raw fetch results to individual JSON files.
/// A failed fetch never overwrites a previous successful cache.
/// </summary>
public sealed class RawSourceCacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;
    private readonly ILogger<RawSourceCacheService> _logger;

    public RawSourceCacheService(
        IOptions<BasketOptions> options,
        ILogger<RawSourceCacheService> logger)
    {
        _dir = options.Value.RawCacheDir;
        _logger = logger;
    }

    public async Task SaveAsync<T>(string sourceName, T data, CancellationToken ct = default)
    {
        try
        {
            EnsureDirectory();
            var path = Path.Combine(_dir, $"{sourceName}.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, data, JsonOpts, ct);
            _logger.LogDebug("Raw cache saved: {Source}", sourceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save raw cache for {Source}", sourceName);
        }
    }

    public async Task<T?> LoadAsync<T>(string sourceName, CancellationToken ct = default)
        where T : class
    {
        try
        {
            var path = Path.Combine(_dir, $"{sourceName}.json");
            if (!File.Exists(path)) return null;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load raw cache for {Source}", sourceName);
            return null;
        }
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_dir))
            Directory.CreateDirectory(_dir);
    }
}
