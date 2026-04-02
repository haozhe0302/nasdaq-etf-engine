using System.Text.Json;
using Hqqq.Api.Modules.Pricing.Contracts;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Persists <see cref="ScaleState"/> to a local JSON file with thread-safe access.
/// When the backing file does not exist, <see cref="ScaleState.Uninitialized"/> is returned.
/// </summary>
public sealed class JsonScaleStateStore : IScaleStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<JsonScaleStateStore> _logger;

    public JsonScaleStateStore(
        IOptions<PricingOptions> options,
        ILogger<JsonScaleStateStore> logger)
    {
        _filePath = options.Value.ScaleStateFilePath;
        _logger = logger;
    }

    public async Task<ScaleState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Scale-state file not found at {Path}; returning uninitialized state", _filePath);
                return ScaleState.Uninitialized;
            }

            await using var stream = File.OpenRead(_filePath);
            var state = await JsonSerializer.DeserializeAsync<ScaleState>(stream, JsonOpts, ct);
            return state ?? ScaleState.Uninitialized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load scale state from {Path}", _filePath);
            return ScaleState.Uninitialized;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(ScaleState state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureDirectory();
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, state, JsonOpts, ct);
            _logger.LogInformation("Scale state persisted to {Path}", _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                _logger.LogInformation("Scale-state file deleted at {Path}", _filePath);
            }
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
