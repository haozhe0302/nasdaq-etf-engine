namespace Hqqq.Api.Configuration;

public sealed class TiingoOptions
{
    public const string SectionName = "Tiingo";

    public string ApiKey { get; set; } = string.Empty;
    public string WebSocketUrl { get; set; } = "wss://api.tiingo.com/iex";
    public string RestBaseUrl { get; set; } = "https://api.tiingo.com/iex";
    public int RestPollingIntervalSeconds { get; set; } = 2;
    public int ReconnectBaseDelaySeconds { get; set; } = 2;
    public int StaleAfterSeconds { get; set; } = 5;
    public int WebSocketThresholdLevel { get; set; } = 6;
}
