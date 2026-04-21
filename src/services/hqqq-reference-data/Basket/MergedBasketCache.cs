using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Last-good merged-basket cache — Phase 2 port of
/// <c>src/hqqq-api/Modules/Basket/Services/BasketCacheService.cs</c>.
/// Persists the most recent successful merged snapshot so the pipeline
/// can recover after a process restart without a fresh upstream fetch.
/// </summary>
/// <remarks>
/// The cache is a single JSON file at
/// <see cref="BasketCacheOptions.MergedCacheFilePath"/>. IO failures are
/// logged and swallowed — the cache degrades to in-memory only and the
/// pipeline continues. Concurrency is serialized with a semaphore so
/// scheduler and REST-triggered writes do not interleave.
/// </remarks>
public sealed class MergedBasketCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly BasketCacheOptions _options;
    private readonly ILogger<MergedBasketCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MergedBasketEnvelope? _memory;

    public MergedBasketCache(IOptions<ReferenceDataOptions> options, ILogger<MergedBasketCache> logger)
    {
        _options = options.Value.Basket.Cache;
        _logger = logger;
    }

    public async Task<MergedBasketEnvelope?> TryLoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_memory is not null) return _memory;

            var path = _options.MergedCacheFilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<MergedBasketEnvelope>(json, SerializerOptions);
                _memory = loaded;
                return loaded;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MergedBasketCache: failed to read {Path}", path);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StoreAsync(MergedBasketEnvelope envelope, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _memory = envelope;

            var path = _options.MergedCacheFilePath;
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(envelope, SerializerOptions),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MergedBasketCache: failed to persist to {Path}", path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Serialization envelope for <see cref="MergedBasketCache"/>.</summary>
public sealed record MergedBasketEnvelope
{
    public required HoldingsSnapshot Snapshot { get; init; }
    public required DateTimeOffset MergedAtUtc { get; init; }
    public required string TailSource { get; init; }
    public required bool IsDegraded { get; init; }
    public required string ContentFingerprint16 { get; init; }
    public int ConstituentCount { get; init; }

    /// <summary>
    /// Anchor adapter name that supplied authoritative shares
    /// (<c>"stockanalysis"</c> or <c>"schwab"</c>), or <c>null</c> when
    /// the merge fell through to the anchor-less proxy path (both
    /// scrapers unavailable/disabled).
    /// </summary>
    public string? AnchorSource { get; init; }

    /// <summary>
    /// True when the active basket carries at least one row with a
    /// positive <c>SharesHeld</c> from an authoritative anchor source.
    /// </summary>
    public bool HasOfficialShares { get; init; }

    /// <summary>
    /// Basket-level mode label: <c>"anchored"</c>, <c>"anchored-proxy-tail"</c>,
    /// or <c>"anchor-less-proxy"</c>. Mirrors the Phase 1 merge-quality
    /// semantics so operator log correlation is direct.
    /// </summary>
    public string BasketMode { get; init; } = "anchored";
}
