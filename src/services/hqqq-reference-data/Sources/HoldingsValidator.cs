using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Validates a normalized <see cref="HoldingsSnapshot"/> before it is
/// promoted to the active basket. Validation rules are the minimum the
/// pricing engine needs to not produce garbage downstream:
/// non-empty universe, no duplicate symbols, positive shares + price,
/// non-null metadata, and a count within the configured soft bounds.
/// </summary>
public sealed class HoldingsValidator
{
    private readonly ValidationOptions _options;

    public HoldingsValidator(IOptions<ReferenceDataOptions> options)
    {
        _options = options.Value.Validation;
    }

    public ValidationOutcome Validate(HoldingsSnapshot snapshot)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(snapshot.BasketId)) errors.Add("basketId is required");
        if (string.IsNullOrWhiteSpace(snapshot.Version)) errors.Add("version is required");
        if (snapshot.ScaleFactor <= 0m) errors.Add("scaleFactor must be > 0");
        if (snapshot.Constituents.Count == 0) errors.Add("constituents must be non-empty");

        var count = snapshot.Constituents.Count;
        if (count > 0)
        {
            if (count < _options.MinConstituents)
                errors.Add($"constituent count {count} < MinConstituents {_options.MinConstituents}");
            if (count > _options.MaxConstituents)
                errors.Add($"constituent count {count} > MaxConstituents {_options.MaxConstituents}");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in snapshot.Constituents)
        {
            if (string.IsNullOrWhiteSpace(c.Symbol))
            {
                errors.Add("constituent has empty symbol");
                continue;
            }
            if (!seen.Add(c.Symbol))
                errors.Add($"duplicate symbol '{c.Symbol}'");
            if (string.IsNullOrWhiteSpace(c.Name))
                errors.Add($"{c.Symbol}: name is required");
            if (string.IsNullOrWhiteSpace(c.Sector))
                errors.Add($"{c.Symbol}: sector is required");
            if (c.SharesHeld <= 0m)
                errors.Add($"{c.Symbol}: sharesHeld must be > 0");
            if (c.ReferencePrice <= 0m)
                errors.Add($"{c.Symbol}: referencePrice must be > 0");
        }

        return new ValidationOutcome(
            IsValid: errors.Count == 0,
            Errors: errors,
            Strict: _options.Strict);
    }

    /// <summary>
    /// Convenience: in strict mode, any error blocks activation; in permissive
    /// mode we still block on hard structural issues (empty / duplicates /
    /// bounds) but tolerate per-row data quality problems.
    /// </summary>
    public bool BlocksActivation(ValidationOutcome outcome)
    {
        if (outcome.IsValid) return false;
        if (outcome.Strict) return true;

        foreach (var err in outcome.Errors)
        {
            if (err.StartsWith("constituents must be non-empty", StringComparison.Ordinal)
                || err.StartsWith("duplicate symbol", StringComparison.Ordinal)
                || err.Contains("< MinConstituents", StringComparison.Ordinal)
                || err.Contains("> MaxConstituents", StringComparison.Ordinal)
                || err.StartsWith("basketId", StringComparison.Ordinal)
                || err.StartsWith("version", StringComparison.Ordinal)
                || err.StartsWith("scaleFactor", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed record ValidationOutcome(bool IsValid, IReadOnlyList<string> Errors, bool Strict);
