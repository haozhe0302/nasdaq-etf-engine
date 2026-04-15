namespace Hqqq.Ingress.Configuration;

/// <summary>
/// Tiingo provider settings, bound to the "Tiingo" configuration section.
/// </summary>
public sealed class TiingoOptions
{
    public string? ApiKey { get; set; }
    public string WebSocketUrl { get; set; } = "wss://api.tiingo.com/iex";
    public string RestBaseUrl { get; set; } = "https://api.tiingo.com/iex";
    public int ReconnectBaseDelaySeconds { get; set; } = 5;
    public int RestPollingIntervalSeconds { get; set; } = 15;
}
