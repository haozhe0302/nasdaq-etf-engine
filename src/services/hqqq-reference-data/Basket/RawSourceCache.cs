using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Per-source raw-payload cache — Phase 2 port of
/// <c>src/hqqq-api/Modules/Basket/Services/RawSourceCacheService.cs</c>.
/// Persists the last-successful live fetch for each adapter so the
/// pipeline can degrade gracefully when a single upstream is
/// temporarily unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Files live under <see cref="BasketCacheOptions.RawCacheDir"/> as
/// <c>{adapterName}.json</c>. When the directory cannot be created or
/// written, the cache degrades to in-memory only; the pipeline is never
/// blocked by cache IO.
/// </para>
/// </remarks>
public sealed class RawSourceCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly BasketCacheOptions _cacheOptions;
    private readonly ILogger<RawSourceCache> _logger;
    private readonly Dictionary<string, RawCacheEntry> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RawSourceCache(IOptions<ReferenceDataOptions> options, ILogger<RawSourceCache> logger)
    {
        _cacheOptions = options.Value.Basket.Cache;
        _logger = logger;
    }

    /// <summary>Persists a successful live payload for the named source.</summary>
    public async Task WriteAsync<TPayload>(string source, TPayload payload, CancellationToken ct)
        where TPayload : class
    {
        var envelope = new RawCacheEntry
        {
            Source = source,
            PayloadJson = JsonSerializer.Serialize(payload, SerializerOptions),
            PayloadType = typeof(TPayload).AssemblyQualifiedName,
            CachedAtUtc = DateTimeOffset.UtcNow,
        };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _memory[source] = envelope;

            var path = TryResolvePath(source);
            if (path is null) return;

            try
            {
                Directory.CreateDirectory(_cacheOptions.RawCacheDir);
                await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(envelope, SerializerOptions),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RawSourceCache: failed to persist {Source} to {Path}", source, path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Attempts to recover the most-recent cached payload for the named source.</summary>
    public async Task<TPayload?> TryReadAsync<TPayload>(string source, CancellationToken ct)
        where TPayload : class
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_memory.TryGetValue(source, out var entry))
                return DeserializePayload<TPayload>(entry);

            var path = TryResolvePath(source);
            if (path is null || !File.Exists(path)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<RawCacheEntry>(json, SerializerOptions);
                if (loaded is null) return null;

                _memory[source] = loaded;
                return DeserializePayload<TPayload>(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RawSourceCache: failed to read {Source} from {Path}", source, path);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public DateTimeOffset? GetCachedAtUtc(string source)
    {
        lock (_memory)
        {
            return _memory.TryGetValue(source, out var e) ? e.CachedAtUtc : null;
        }
    }

    private static TPayload? DeserializePayload<TPayload>(RawCacheEntry entry)
        where TPayload : class
    {
        if (string.IsNullOrEmpty(entry.PayloadJson)) return null;
        return JsonSerializer.Deserialize<TPayload>(entry.PayloadJson, SerializerOptions);
    }

    private string? TryResolvePath(string source)
    {
        if (string.IsNullOrWhiteSpace(_cacheOptions.RawCacheDir)) return null;

        var safeName = string.Concat(source.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrEmpty(safeName)) return null;

        return Path.Combine(_cacheOptions.RawCacheDir, safeName + ".json");
    }

    private sealed record RawCacheEntry
    {
        public required string Source { get; init; }
        public required string PayloadJson { get; init; }
        public string? PayloadType { get; init; }
        public required DateTimeOffset CachedAtUtc { get; init; }
    }
}
