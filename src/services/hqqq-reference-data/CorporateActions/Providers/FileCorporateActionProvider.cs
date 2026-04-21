using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.CorporateActions.Providers;

/// <summary>
/// Deterministic, offline-safe corporate-action provider. Reads a JSON
/// file with <c>{ "splits": [...], "renames": [...] }</c> shape from
/// either a configured filesystem path
/// (<c>ReferenceData:CorporateActions:File:Path</c>) or, when unset, the
/// embedded resource <c>Resources/corporate-actions-seed.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The provider is load-once-and-cache: the file is read at construction
/// time (or lazily on first <see cref="FetchAsync"/> call) and held in
/// memory. Operators who edit the file must restart the service — this
/// matches the existing <c>BasketSeedLoader</c> pattern so the behaviour
/// is predictable.
/// </para>
/// </remarks>
public sealed class FileCorporateActionProvider : ICorporateActionProvider
{
    private const string EmbeddedResourceName = "Hqqq.ReferenceData.Resources.corporate-actions-seed.json";

    private readonly FileCorporateActionOptions _options;
    private readonly ILogger<FileCorporateActionProvider> _logger;
    private readonly Lazy<CorporateActionFileSchema> _schema;

    public FileCorporateActionProvider(
        IOptions<ReferenceDataOptions> options,
        ILogger<FileCorporateActionProvider> logger)
    {
        _options = options.Value.CorporateActions.File;
        _logger = logger;
        _schema = new Lazy<CorporateActionFileSchema>(LoadSchema);
    }

    public string Name => "file";

    public Task<CorporateActionFeed> FetchAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        CorporateActionFileSchema schema;
        try
        {
            schema = _schema.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "FileCorporateActionProvider: failed to load file; returning empty feed");
            return Task.FromResult(new CorporateActionFeed
            {
                Splits = Array.Empty<SplitEvent>(),
                Renames = Array.Empty<SymbolRenameEvent>(),
                Source = Name,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Error = ex.Message,
            });
        }

        var symbolSet = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var splits = schema.Splits
            .Select(row => TryMapSplit(row))
            .Where(s => s is not null)
            .Select(s => s!)
            .Where(s => s.EffectiveDate >= from && s.EffectiveDate <= to)
            .Where(s => symbolSet.Count == 0 || symbolSet.Contains(s.Symbol))
            .ToList();

        var renames = schema.Renames
            .Select(row => TryMapRename(row))
            .Where(r => r is not null)
            .Select(r => r!)
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            // renames can affect either the old or new symbol so we keep
            // them all — the adjustment service handles filtering against
            // the actual snapshot.
            .ToList();

        return Task.FromResult(new CorporateActionFeed
        {
            Splits = splits,
            Renames = renames,
            Source = Name,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private SplitEvent? TryMapSplit(CorporateActionFileSchema.SplitRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Symbol)) return null;
        if (!TryParseDate(row.EffectiveDate, out var date)) return null;
        if (row.Factor <= 0m || row.Factor == 1m) return null;

        return new SplitEvent
        {
            Symbol = row.Symbol.Trim().ToUpperInvariant(),
            EffectiveDate = date,
            Factor = row.Factor,
            Description = row.Description,
            Source = Name,
        };
    }

    private SymbolRenameEvent? TryMapRename(CorporateActionFileSchema.RenameRow row)
    {
        if (string.IsNullOrWhiteSpace(row.OldSymbol) || string.IsNullOrWhiteSpace(row.NewSymbol))
            return null;
        if (string.Equals(row.OldSymbol.Trim(), row.NewSymbol.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        if (!TryParseDate(row.EffectiveDate, out var date)) return null;

        return new SymbolRenameEvent
        {
            OldSymbol = row.OldSymbol.Trim().ToUpperInvariant(),
            NewSymbol = row.NewSymbol.Trim().ToUpperInvariant(),
            EffectiveDate = date,
            Description = row.Description,
            Source = Name,
        };
    }

    private static bool TryParseDate(string raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private CorporateActionFileSchema LoadSchema()
    {
        var (json, source) = ReadJson();
        CorporateActionFileSchema? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CorporateActionFileSchema>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"FileCorporateActionProvider: failed to parse corp-actions JSON from {source}: {ex.Message}",
                ex);
        }

        var schema = parsed ?? new CorporateActionFileSchema();
        _logger.LogInformation(
            "FileCorporateActionProvider: loaded {Splits} splits, {Renames} renames from {Source}",
            schema.Splits.Count, schema.Renames.Count, source);
        return schema;
    }

    private (string Json, string SourcePath) ReadJson()
    {
        var overridePath = _options.Path;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new InvalidOperationException(
                    $"FileCorporateActionProvider: ReferenceData:CorporateActions:File:Path='{overridePath}' does not exist.");
            }
            return (File.ReadAllText(overridePath), overridePath);
        }

        var asm = typeof(FileCorporateActionProvider).Assembly;
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
}
