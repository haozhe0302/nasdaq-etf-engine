namespace Hqqq.Api.Configuration;

public sealed class BasketOptions
{
    public const string SectionName = "Basket";

    public string HoldingsSourceUrl { get; set; } =
        "https://api.nasdaq.com/api/quote/list-type/nasdaq100";

    public string AlphaVantageApiKey { get; set; } = "";

    public string AlphaVantageBaseUrl { get; set; } =
        "https://www.alphavantage.co/query";

    public string RefreshTimeLocal { get; set; } = "08:00";
    public string CacheFilePath { get; set; } = "data/basket-cache.json";
    public string RawCacheDir { get; set; } = "data/raw";
    public string MergedHistoryDir { get; set; } = "data/merged";
}
