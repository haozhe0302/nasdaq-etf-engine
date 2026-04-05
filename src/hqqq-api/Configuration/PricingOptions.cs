namespace Hqqq.Api.Configuration;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public string ScaleStateFilePath { get; set; } = "data/scale-state.json";
    public string SeriesFilePath { get; set; } = "data/series.json";
    public int QuoteBroadcastIntervalMs { get; set; } = 1000;
    public int SeriesCapacity { get; set; } = 5_000;
    public int SeriesRecordIntervalMs { get; set; } = 5_000;
    public string MarketTimeZone { get; set; } = "America/New_York";
    public string HistoryDir { get; set; } = "data/history";
    public string NyseCalendarFilePath { get; set; } = "data/nyse-calendar.json";
}
