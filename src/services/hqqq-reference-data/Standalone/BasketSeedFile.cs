using System.Text.Json.Serialization;

namespace Hqqq.ReferenceData.Standalone;

/// <summary>
/// Wire-shape DTOs for the deterministic basket seed JSON consumed by
/// <see cref="BasketSeedLoader"/>. Decoupled from the domain entities so
/// the seed schema can evolve without touching the engine contracts.
/// </summary>
public sealed class BasketSeedFile
{
    [JsonPropertyName("basketId")]
    public string BasketId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("asOfDate")]
    public string AsOfDate { get; set; } = string.Empty;

    [JsonPropertyName("scaleFactor")]
    public decimal ScaleFactor { get; set; }

    [JsonPropertyName("navPreviousClose")]
    public decimal? NavPreviousClose { get; set; }

    [JsonPropertyName("qqqPreviousClose")]
    public decimal? QqqPreviousClose { get; set; }

    [JsonPropertyName("constituents")]
    public List<BasketSeedConstituent> Constituents { get; set; } = new();
}

public sealed class BasketSeedConstituent
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sector")]
    public string Sector { get; set; } = string.Empty;

    [JsonPropertyName("sharesHeld")]
    public decimal SharesHeld { get; set; }

    [JsonPropertyName("referencePrice")]
    public decimal ReferencePrice { get; set; }

    [JsonPropertyName("targetWeight")]
    public decimal? TargetWeight { get; set; }
}
