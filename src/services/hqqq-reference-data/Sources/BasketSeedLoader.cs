using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Loads the deterministic basket seed shipped with <c>hqqq-reference-data</c>.
/// Resolves from <see cref="ReferenceDataOptions.SeedPath"/> when set,
/// otherwise from the embedded resource <c>Resources/basket-seed.json</c>.
///
/// Always throws <see cref="InvalidOperationException"/> with an
/// operator-friendly message on any structural problem so standalone
/// startup fails fast (the host exits, the orchestrator restarts, the
/// operator sees the validation message in logs).
/// </summary>
public sealed class BasketSeedLoader
{
    private const string EmbeddedResourceName = "Hqqq.ReferenceData.Resources.basket-seed.json";

    private readonly ReferenceDataOptions _options;
    private readonly ILogger<BasketSeedLoader> _logger;

    public BasketSeedLoader(
        IOptions<ReferenceDataOptions> options,
        ILogger<BasketSeedLoader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Reads + validates the seed and returns it as a normalized
    /// <see cref="HoldingsSnapshot"/> tagged with <c>Source = "fallback-seed"</c>.
    /// </summary>
    public HoldingsSnapshot Load()
    {
        var (json, source) = ReadJson();
        var file = Deserialize(json, source);
        ValidateStructural(file, source);

        var asOfDate = ParseAsOfDate(file.AsOfDate, source);

        var constituents = file.Constituents
            .Select(c => new HoldingsConstituent
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                Name = c.Name,
                Sector = c.Sector,
                SharesHeld = c.SharesHeld,
                ReferencePrice = c.ReferencePrice,
                TargetWeight = c.TargetWeight,
            })
            .ToArray();

        var snapshot = new HoldingsSnapshot
        {
            BasketId = file.BasketId,
            Version = file.Version,
            AsOfDate = asOfDate,
            ScaleFactor = file.ScaleFactor,
            NavPreviousClose = file.NavPreviousClose,
            QqqPreviousClose = file.QqqPreviousClose,
            Constituents = constituents,
            Source = SourceTag,
        };

        _logger.LogInformation(
            "Basket seed loaded from {SourcePath} — basketId={BasketId} version={Version} asOfDate={AsOfDate} constituents={Count}",
            source, file.BasketId, file.Version, asOfDate, file.Constituents.Count);

        return snapshot;
    }

    /// <summary>Canonical lineage tag emitted by this loader.</summary>
    public const string SourceTag = "fallback-seed";

    private (string Json, string SourcePath) ReadJson()
    {
        var overridePath = _options.SeedPath;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new InvalidOperationException(
                    $"BasketSeedLoader: ReferenceData:SeedPath='{overridePath}' does not exist.");
            }
            return (File.ReadAllText(overridePath), overridePath);
        }

        var asm = typeof(BasketSeedLoader).Assembly;
        return (LoadEmbedded(asm), $"resource://{EmbeddedResourceName}");
    }

    /// <summary>Public for tests so they can verify the embedded resource is wired up.</summary>
    public static string LoadEmbedded(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' not found. Check hqqq-reference-data.csproj <EmbeddedResource>.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static HoldingsFileSchema Deserialize(string json, string source)
    {
        HoldingsFileSchema? file;
        try
        {
            file = JsonSerializer.Deserialize<HoldingsFileSchema>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"BasketSeedLoader: failed to parse seed JSON from {source}: {ex.Message}", ex);
        }

        return file ?? throw new InvalidOperationException(
            $"BasketSeedLoader: seed JSON from {source} deserialized to null.");
    }

    /// <summary>
    /// Structural-only validation (required fields present + shape is parsable).
    /// Semantic validation (bounds, duplicates, positive shares) is the
    /// <see cref="HoldingsValidator"/>'s job so live + seed go through the same
    /// gate. We still check here because the seed is meant to fail fast at
    /// startup with a targeted error message.
    /// </summary>
    private static void ValidateStructural(HoldingsFileSchema file, string source)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(file.BasketId)) errors.Add("basketId is required");
        if (string.IsNullOrWhiteSpace(file.Version)) errors.Add("version is required");
        if (string.IsNullOrWhiteSpace(file.AsOfDate)) errors.Add("asOfDate is required");
        if (file.ScaleFactor <= 0m) errors.Add("scaleFactor must be > 0");
        if (file.Constituents is null || file.Constituents.Count == 0)
            errors.Add("constituents must be non-empty");

        if (file.Constituents is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in file.Constituents)
            {
                if (string.IsNullOrWhiteSpace(c.Symbol))
                {
                    errors.Add("constituent has empty symbol");
                    continue;
                }
                if (!seen.Add(c.Symbol))
                    errors.Add($"duplicate symbol '{c.Symbol}'");
                if (c.ReferencePrice <= 0m)
                    errors.Add($"{c.Symbol}: referencePrice must be > 0");
                if (c.SharesHeld <= 0m)
                    errors.Add($"{c.Symbol}: sharesHeld must be > 0");
                if (string.IsNullOrWhiteSpace(c.Name))
                    errors.Add($"{c.Symbol}: name is required");
                if (string.IsNullOrWhiteSpace(c.Sector))
                    errors.Add($"{c.Symbol}: sector is required");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"BasketSeedLoader: seed from {source} failed validation: " +
                string.Join("; ", errors));
        }
    }

    private static DateOnly ParseAsOfDate(string raw, string source)
    {
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException(
            $"BasketSeedLoader: seed from {source} has invalid asOfDate '{raw}' (expected yyyy-MM-dd).");
    }
}
