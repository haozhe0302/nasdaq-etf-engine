using System.Text.Json;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Pricing.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Pricing.Services;

public sealed class JsonSeriesStore : ISeriesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<JsonSeriesStore> _logger;

    public JsonSeriesStore(
        IOptions<PricingOptions> options,
        ILogger<JsonSeriesStore> logger)
    {
        _filePath = options.Value.SeriesFilePath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SeriesPoint>> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Series file not found at {Path}", _filePath);
                return [];
            }

            await using var stream = File.OpenRead(_filePath);
            var points = await JsonSerializer.DeserializeAsync<List<SeriesPoint>>(stream, JsonOpts, ct);
            _logger.LogInformation("Loaded {Count} series points from {Path}", points?.Count ?? 0, _filePath);
            return points ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load series from {Path}", _filePath);
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<SeriesPoint> points, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureDirectory();
            var tmpPath = _filePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, points, JsonOpts, ct);
            }
            File.Move(tmpPath, _filePath, overwrite: true);
            _logger.LogDebug("Persisted {Count} series points to {Path}", points.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save series to {Path}", _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
