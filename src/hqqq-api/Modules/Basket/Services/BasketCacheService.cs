using System.Text.Json;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Thread-safe JSON file cache for merged <see cref="BasketSnapshot"/>.
/// Enforces a 3-day max age for cache reads.
/// Maintains a rolling history of the 3 most recent successful baskets.
/// </summary>
public sealed class BasketCacheService
{
    public const int MaxCacheAgeDays = 3;
    private const int RollingHistoryCount = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly string _historyDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<BasketCacheService> _logger;

    public BasketCacheService(
        IOptions<BasketOptions> options,
        ILogger<BasketCacheService> logger)
    {
        _filePath = options.Value.CacheFilePath;
        _historyDir = options.Value.MergedHistoryDir;
        _logger = logger;
    }

    public async Task<BasketSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Basket cache not found at {Path}", _filePath);
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            var snapshot = await JsonSerializer.DeserializeAsync<BasketSnapshot>(stream, JsonOpts, ct);
            if (snapshot is null) return null;

            var age = DateTimeOffset.UtcNow - snapshot.FetchedAtUtc;
            if (age.TotalDays > MaxCacheAgeDays)
            {
                _logger.LogWarning("Basket cache expired: age {Age:F1}d > {Max}d", age.TotalDays, MaxCacheAgeDays);
                return null;
            }

            return snapshot with
            {
                Source = snapshot.Source with { SourceType = "cache", CacheAge = age },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load basket cache from {Path}", _filePath);
            return null;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(BasketSnapshot snapshot, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureDir(_filePath);
            var toSave = snapshot with
            {
                Source = snapshot.Source with { CacheWrittenAtUtc = DateTimeOffset.UtcNow },
            };

            await using (var stream = File.Create(_filePath))
            {
                await JsonSerializer.SerializeAsync(stream, toSave, JsonOpts, ct);
            }

            SaveRollingHistory(toSave);

            _logger.LogInformation("Basket cache saved: {Count} constituents", snapshot.Constituents.Count);
        }
        finally { _lock.Release(); }
    }

    private void SaveRollingHistory(BasketSnapshot snapshot)
    {
        try
        {
            EnsureDir(Path.Combine(_historyDir, "_"));
            var stamp = snapshot.FetchedAtUtc.ToString("yyyyMMdd-HHmmss");
            var histPath = Path.Combine(_historyDir, $"basket-{stamp}.json");

            using var stream = File.Create(histPath);
            JsonSerializer.Serialize(stream, snapshot, JsonOpts);

            var files = Directory.GetFiles(_historyDir, "basket-*.json")
                .OrderByDescending(f => f)
                .Skip(RollingHistoryCount)
                .ToList();

            foreach (var old in files)
            {
                try { File.Delete(old); }
                catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rolling history save failed");
        }
    }

    private static void EnsureDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
