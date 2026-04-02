namespace Hqqq.Api.Configuration;

public sealed class BasketOptions
{
    public const string SectionName = "Basket";

    public string HoldingsSourceUrl { get; set; } = string.Empty;
    public string RefreshTimeLocal { get; set; } = "18:30";
    public string CacheFilePath { get; set; } = "data/basket-cache.json";
}
