using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Standalone;

/// <summary>
/// Loads the deterministic basket seed used by standalone-mode
/// <c>hqqq-reference-data</c>. Resolves from
/// <see cref="BasketSeedOptions.SeedPath"/> when set, otherwise from the
/// embedded resource <c>Resources/basket-seed.json</c>. Validates the
/// shape and throws <see cref="InvalidOperationException"/> with an
/// operator-friendly message on any failure.
/// </summary>
public sealed class BasketSeedLoader
{
    private const string EmbeddedResourceName = "Hqqq.ReferenceData.Resources.basket-seed.json";

    private readonly BasketSeedOptions _options;
    private readonly ILogger<BasketSeedLoader> _logger;

    public BasketSeedLoader(
        IOptions<BasketSeedOptions> options,
        ILogger<BasketSeedLoader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Loads + validates the seed. Throws on any structural problem so
    /// standalone startup fails fast (the host will exit, the orchestrator
    /// will restart, and the operator sees the validation message in logs).
    /// </summary>
    public BasketSeed Load()
    {
        var (json, source) = ReadJson();
        var file = Deserialize(json, source);
        Validate(file, source);

        var asOfDate = ParseAsOfDate(file.AsOfDate, source);
        var fingerprint = ComputeFingerprint(file);

        _logger.LogInformation(
            "Basket seed loaded from {Source} — basketId={BasketId} version={Version} asOfDate={AsOfDate} fingerprint={Fingerprint} constituents={Count}",
            source, file.BasketId, file.Version, asOfDate, fingerprint, file.Constituents.Count);

        return new BasketSeed
        {
            BasketId = file.BasketId,
            Version = file.Version,
            AsOfDate = asOfDate,
            ScaleFactor = file.ScaleFactor,
            NavPreviousClose = file.NavPreviousClose,
            QqqPreviousClose = file.QqqPreviousClose,
            Constituents = file.Constituents,
            Fingerprint = fingerprint,
            Source = source,
        };
    }

    private (string Json, string Source) ReadJson()
    {
        var overridePath = _options.SeedPath;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new InvalidOperationException(
                    $"BasketSeedLoader: ReferenceData:Standalone:SeedPath='{overridePath}' does not exist.");
            }
            return (File.ReadAllText(overridePath), overridePath);
        }

        var asm = typeof(BasketSeedLoader).Assembly;
        return (LoadEmbedded(asm), $"resource://{EmbeddedResourceName}");
    }

    /// <summary>Public for unit tests so they can verify the embedded resource is wired up.</summary>
    public static string LoadEmbedded(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' not found. Check hqqq-reference-data.csproj <EmbeddedResource>.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static BasketSeedFile Deserialize(string json, string source)
    {
        BasketSeedFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BasketSeedFile>(json, new JsonSerializerOptions
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

    private static void Validate(BasketSeedFile file, string source)
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

    /// <summary>
    /// Deterministic SHA-256 of a canonical projection of the seed
    /// (basketId + version + asOfDate + scaleFactor + ordered constituents).
    /// Two processes loading the same seed file produce the same
    /// fingerprint, which is what the quote-engine's idempotency guard
    /// relies on so a re-publish doesn't reset state.
    /// </summary>
    public static string ComputeFingerprint(BasketSeedFile file)
    {
        var canonical = new
        {
            basketId = file.BasketId,
            version = file.Version,
            asOfDate = file.AsOfDate,
            scaleFactor = file.ScaleFactor,
            navPreviousClose = file.NavPreviousClose,
            qqqPreviousClose = file.QqqPreviousClose,
            constituents = file.Constituents
                .OrderBy(c => c.Symbol, StringComparer.Ordinal)
                .Select(c => new
                {
                    symbol = c.Symbol,
                    name = c.Name,
                    sector = c.Sector,
                    sharesHeld = c.SharesHeld,
                    referencePrice = c.ReferencePrice,
                    targetWeight = c.TargetWeight,
                })
                .ToArray(),
        };

        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }
}
