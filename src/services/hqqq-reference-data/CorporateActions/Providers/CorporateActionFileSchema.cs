using System.Text.Json.Serialization;

namespace Hqqq.ReferenceData.CorporateActions.Providers;

/// <summary>
/// On-disk JSON shape for the file-based corporate-action provider.
/// Matches the <c>Resources/corporate-actions-seed.json</c> schema.
/// </summary>
internal sealed class CorporateActionFileSchema
{
    [JsonPropertyName("splits")]
    public List<SplitRow> Splits { get; set; } = new();

    [JsonPropertyName("renames")]
    public List<RenameRow> Renames { get; set; } = new();

    public sealed class SplitRow
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("effectiveDate")]
        public string EffectiveDate { get; set; } = string.Empty;

        [JsonPropertyName("factor")]
        public decimal Factor { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class RenameRow
    {
        [JsonPropertyName("oldSymbol")]
        public string OldSymbol { get; set; } = string.Empty;

        [JsonPropertyName("newSymbol")]
        public string NewSymbol { get; set; } = string.Empty;

        [JsonPropertyName("effectiveDate")]
        public string EffectiveDate { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
