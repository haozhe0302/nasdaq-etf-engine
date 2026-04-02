namespace Hqqq.Api.Configuration;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public string ScaleStateFilePath { get; set; } = "data/scale-state.json";
    public int QuoteBroadcastIntervalMs { get; set; } = 1000;
    public int SeriesCapacity { get; set; } = 120;
    public string MarketTimeZone { get; set; } = "America/New_York";
}
